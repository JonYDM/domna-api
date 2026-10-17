using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Cada línea de un pedido y cada renglón de "Lo más apartado" lleva la foto de la prenda, para que las
/// listas muestren la foto real sin pedir el catálogo. Es solo una URL: la imagen la baja el navegador.
/// </summary>
public class FotosEnPedidosTests
{
    private readonly RelojFijo _reloj = new();
    private readonly ProductoRepoFake _productos = new();
    private readonly ApartadoRepoFake _apartados = new();
    private readonly ClientaRepoFake _clientas;
    private readonly ApartadoService _svc;

    public FotosEnPedidosTests()
    {
        var ana = Clienta.Crear("c1", "Ana", "Lopez", "7771234567", "ana@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        _clientas = new ClientaRepoFake(ana);
        _svc = new ApartadoService(_apartados, _productos, _clientas, new ConfigRepoFake(), _reloj);
    }

    /// <summary>Prenda "Rosa" + "Negro" con una variante por color: v-{id}-rosa y v-{id}-negro.</summary>
    private async Task<Producto> Prenda(string id)
    {
        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { 10, 10 } },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = new[] { 10, 10 } },
        };
        var n = 0;
        var v = 0;
        var p = Producto.Crear(
            id, $"Vestido {id}", null, "vestidos", Silueta.Vestido, 600m, 250m, true,
            new[] { new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa), new ColorDraft("Negro", "#1F1B24", FamiliaColor.Negro) },
            new[] { "M" }, stock, _reloj.Ahora,
            nuevoIdColor: () => $"{id}-c{++n}", nuevoIdVariante: () => $"v-{id}-{(++v == 1 ? "rosa" : "negro")}");
        await _productos.AgregarAsync(p);
        return p;
    }

    private static CrearApartadoInput Pedido(string varianteId, string modalidad = "compra") =>
        new(new List<LineaCarritoInput> { new(varianteId, 1) }, modalidad, "temixco", "fisico", "c1", "efectivo");

    [Fact]
    public async Task La_linea_trae_la_foto_del_color_que_se_compro()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[]
        {
            ("https://r2/p1/rosa-1/800.webp", (string?)"p1-c1"),
            ("https://r2/p1/negro-1/800.webp", "p1-c2"),
        });

        var negro = await _svc.CrearAsync("c1", Pedido("v-p1-negro"));

        Assert.Equal("https://r2/p1/negro-1/800.webp", Assert.Single(negro.Lineas).ImagenUrl);
    }

    [Fact]
    public async Task Con_varias_fotos_del_color_usa_la_primera_por_orden()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[]
        {
            ("https://r2/p1/rosa-1/800.webp", (string?)"p1-c1"),
            ("https://r2/p1/rosa-2/800.webp", "p1-c1"),
        });

        var pedido = await _svc.CrearAsync("c1", Pedido("v-p1-rosa"));

        Assert.Equal("https://r2/p1/rosa-1/800.webp", pedido.Lineas[0].ImagenUrl);
    }

    [Fact]
    public async Task Si_el_color_no_tiene_fotos_usa_la_primera_de_la_prenda()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[] { ("https://r2/p1/rosa-1/800.webp", (string?)"p1-c1") });

        var negro = await _svc.CrearAsync("c1", Pedido("v-p1-negro"));

        Assert.Equal("https://r2/p1/rosa-1/800.webp", negro.Lineas[0].ImagenUrl);
    }

    [Fact]
    public async Task Sin_fotos_la_linea_no_trae_url_y_no_falla()
    {
        await Prenda("p1");
        var pedido = await _svc.CrearAsync("c1", Pedido("v-p1-rosa"));
        Assert.Null(pedido.Lineas[0].ImagenUrl);
    }

    [Fact]
    public async Task Si_cambian_la_foto_de_la_prenda_el_pedido_ya_hecho_la_refleja()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[] { ("https://r2/p1/vieja/800.webp", (string?)"p1-c1") });
        var pedido = await _svc.CrearAsync("c1", Pedido("v-p1-rosa"));

        p.FijarImagenes(new[] { ("https://r2/p1/nueva/800.webp", (string?)"p1-c1") });
        var otraVez = await _svc.ObtenerAsync(pedido.Id);

        Assert.Equal("https://r2/p1/nueva/800.webp", otraVez.Lineas[0].ImagenUrl);
    }

    [Fact]
    public async Task Listar_resuelve_la_foto_de_cada_prenda_distinta()
    {
        var a = await Prenda("p1");
        var b = await Prenda("p2");
        a.FijarImagenes(new[] { ("https://r2/p1/x/800.webp", (string?)"p1-c1") });
        b.FijarImagenes(new[] { ("https://r2/p2/y/800.webp", (string?)"p2-c1") });
        await _svc.CrearAsync("c1", Pedido("v-p1-rosa"));
        await _svc.CrearAsync("c1", Pedido("v-p2-rosa"));

        var lista = await _svc.ListarAsync(null, null, null);

        Assert.Equal(2, lista.Count);
        Assert.Contains(lista, x => x.Lineas[0].ImagenUrl == "https://r2/p1/x/800.webp");
        Assert.Contains(lista, x => x.Lineas[0].ImagenUrl == "https://r2/p2/y/800.webp");
    }

    [Fact]
    public async Task Una_prenda_ya_eliminada_deja_el_pedido_sin_foto_pero_visible()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[] { ("https://r2/p1/x/800.webp", (string?)"p1-c1") });
        var pedido = await _svc.CrearAsync("c1", Pedido("v-p1-rosa"));

        await _productos.EliminarAsync(p);
        var otraVez = await _svc.ObtenerAsync(pedido.Id);

        Assert.Null(otraVez.Lineas[0].ImagenUrl);
        Assert.Equal(pedido.Folio, otraVez.Folio);
    }

    [Fact]
    public async Task Lo_mas_apartado_trae_la_foto_del_color_vendido()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[]
        {
            ("https://r2/p1/rosa-1/800.webp", (string?)"p1-c1"),
            ("https://r2/p1/negro-1/800.webp", "p1-c2"),
        });
        await _svc.CrearAsync("c1", Pedido("v-p1-negro"));

        var m = await _svc.MetricasAsync(null);

        var top = Assert.Single(m.TopProductos);
        Assert.Equal("https://r2/p1/negro-1/800.webp", top.ImagenUrl);
    }

    [Fact]
    public async Task Lo_mas_apartado_de_una_prenda_inactiva_tambien_trae_su_foto()
    {
        var p = await Prenda("p1");
        p.FijarImagenes(new[] { ("https://r2/p1/x/800.webp", (string?)"p1-c1") });
        await _svc.CrearAsync("c1", Pedido("v-p1-rosa"));
        p.Desactivar();

        var m = await _svc.MetricasAsync(null);

        Assert.Equal("https://r2/p1/x/800.webp", Assert.Single(m.TopProductos).ImagenUrl);
    }
}
