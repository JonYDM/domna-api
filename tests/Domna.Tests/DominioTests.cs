using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class DominioTests
{
    private static readonly DateTime Ahora = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── Normalización ──

    [Theory]
    [InlineData("7771234567", "777 123 4567")]
    [InlineData("777 123 4567", "777 123 4567")]
    [InlineData("+52 777 123 4567", "777 123 4567")]
    [InlineData("527771234567", "777 123 4567")]
    public void NormalizarTelefono_formatea_a_diez_digitos(string entrada, string esperado)
    {
        Assert.Equal(esperado, Normalizacion.NormalizarTelefono(entrada));
    }

    [Theory]
    [InlineData("12345")]    // 5 dígitos
    [InlineData("abcdefghij")]
    public void NormalizarTelefono_rechaza_invalidos(string entrada)
    {
        var ex = Assert.Throws<DomnaException>(() => Normalizacion.NormalizarTelefono(entrada));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("12345")]   // 5 dígitos
    [InlineData("1234567")] // 7 dígitos
    [InlineData("abcdef")]
    public void NormalizarPin_exige_seis_digitos(string pin)
    {
        Assert.Throws<DomnaException>(() => Normalizacion.NormalizarPin(pin));
    }

    [Fact]
    public void NormalizarEmail_pasa_a_minusculas()
    {
        Assert.Equal("maria@correo.com", Normalizacion.NormalizarEmail("  Maria@Correo.COM "));
    }

    [Fact]
    public void NormalizarEmail_rechaza_formato_invalido()
    {
        Assert.Throws<DomnaException>(() => Normalizacion.NormalizarEmail("no-es-correo"));
    }

    // ── Variantes talla × color ──

    private static Producto CrearPrenda(bool conCodigos = false)
    {
        return Producto.Crear(
            "p1",
            "Vestido Flor",
            "desc",
            "vestidos",
            Silueta.Vestido,
            precio: 500m,
            costo: 200m,
            permiteApartado: true,
            colores: new[]
            {
                new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa),
                new ColorDraft("Azul", "#4A6A92", FamiliaColor.Azul)
            },
            tallas: new[] { "CH", "M" },
            stock: new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
            {
                [SucursalId.Temixco] = new Dictionary<string, int[]>
                {
                    ["CH"] = new[] { 3, 1 },
                    ["M"] = new[] { 2, 0 }
                },
                [SucursalId.Azteca] = new Dictionary<string, int[]>
                {
                    ["CH"] = new[] { 0, 2 },
                    ["M"] = new[] { 1, 0 }
                }
            },
            ahora: Ahora,
            codigoBarras: conCodigos
                ? new Dictionary<string, string?[]>
                {
                    ["CH"] = new string?[] { "111", "222" },
                    ["M"] = new string?[] { "333", "444" }
                }
                : null);
    }

    [Fact]
    public void Crear_genera_producto_cartesiano_talla_por_color()
    {
        var p = CrearPrenda();
        // 2 tallas × 2 colores = 4 variantes.
        Assert.Equal(4, p.Variantes.Count);
        Assert.Equal(2, p.Colores.Count);
        Assert.Equal(new[] { "CH", "M" }, p.Tallas);
    }

    [Fact]
    public void Crear_asigna_stock_independiente_por_sucursal()
    {
        var p = CrearPrenda();
        var chRosa = p.Variantes.First(v => v.Talla == "CH" && p.Colores.First(c => c.Id == v.ColorId).Nombre == "Rosa");
        Assert.Equal(3, chRosa.StockDe(SucursalId.Temixco));
        Assert.Equal(0, chRosa.StockDe(SucursalId.Azteca));
        Assert.Equal(3, chRosa.StockTotal);
    }

    [Fact]
    public void Crear_genera_sku_unico_por_variante()
    {
        var p = CrearPrenda();
        var skus = p.Variantes.Select(v => v.Sku).ToList();
        Assert.Equal(skus.Count, skus.Distinct().Count());
        Assert.All(skus, s => Assert.StartsWith("VESTIDO-FLOR", s));
    }

    [Fact]
    public void Crear_asigna_codigos_de_barras_por_variante()
    {
        var p = CrearPrenda(conCodigos: true);
        var v = p.BuscarPorCodigoBarras("222");
        Assert.NotNull(v);
        Assert.Equal("CH", v!.Talla);
    }

    [Fact]
    public void Crear_rechaza_precio_no_positivo()
    {
        var ex = Assert.Throws<DomnaException>(() => Producto.Crear(
            "p", "Blusa", "d", "cat", Silueta.Blusa, precio: 0m, costo: 10m, permiteApartado: true,
            colores: new[] { new ColorDraft("Rojo", "#C0283E", FamiliaColor.Rojo) },
            tallas: new[] { "M" },
            stock: new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>(),
            ahora: Ahora));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void AjustarStock_a_variante_inexistente_lanza_404()
    {
        var p = CrearPrenda();
        var ex = Assert.Throws<DomnaException>(() => p.AjustarStock("no-existe", SucursalId.Temixco, 5, Ahora));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void AjustarStock_de_cero_a_positivo_marca_reabastecido()
    {
        var p = CrearPrenda();
        var agotada = p.Variantes.First(v => v.StockTotal == 0);
        Assert.Null(p.ReabastecidoEl);
        p.AjustarStock(agotada.Id, SucursalId.Temixco, 4, Ahora);
        Assert.Equal(Ahora, p.ReabastecidoEl);
    }

    [Fact]
    public void FijarStock_negativo_lanza_400()
    {
        var p = CrearPrenda();
        var v = p.Variantes.First();
        Assert.Throws<DomnaException>(() => p.AjustarStock(v.Id, SucursalId.Temixco, -1, Ahora));
    }

    [Fact]
    public void Desactivar_hace_baja_logica()
    {
        var p = CrearPrenda();
        Assert.True(p.Activo);
        p.Desactivar();
        Assert.False(p.Activo);
    }

    [Fact]
    public void AgregarColor_crea_variantes_y_fotos_del_color()
    {
        var p = CrearPrenda(); // tiene Rosa y Azul, tallas CH y M
        var antesVariantes = p.Variantes.Count;

        p.AgregarColor(
            new ColorDraft("Verde", "#5E7A4E", FamiliaColor.Verde),
            new[] { "CH", "M" },
            new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
            {
                [SucursalId.Temixco] = new Dictionary<string, int[]> { ["CH"] = new[] { 4 }, ["M"] = new[] { 2 } },
                [SucursalId.Azteca] = new Dictionary<string, int[]> { ["CH"] = new[] { 0 }, ["M"] = new[] { 1 } },
            },
            new[] { "https://cdn/verde-1.webp", "https://cdn/verde-2.webp" },
            Ahora);

        Assert.Equal(3, p.Colores.Count);
        Assert.Equal(antesVariantes + 2, p.Variantes.Count); // +2 tallas del nuevo color
        var verde = p.Colores.First(c => c.Nombre == "Verde");
        Assert.Equal(2, p.Imagenes.Count(i => i.ColorId == verde.Id));
        var chVerde = p.Variantes.First(v => v.ColorId == verde.Id && v.Talla == "CH");
        Assert.Equal(4, chVerde.StockDe(SucursalId.Temixco));
    }

    [Fact]
    public void AgregarColor_rechaza_color_duplicado()
    {
        var p = CrearPrenda();
        var ex = Assert.Throws<DomnaException>(() => p.AgregarColor(
            new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa),
            new[] { "CH" },
            new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>(),
            new string[0],
            Ahora));
        Assert.Equal(409, ex.StatusCode);
    }
}
