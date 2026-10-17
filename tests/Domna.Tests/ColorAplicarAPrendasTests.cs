using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Un color del catálogo y el que usan las prendas es EL MISMO color: si se edita en el catálogo, debe
/// cambiar en todas las prendas que lo usan. Cada prenda guarda su propia copia (así borrar un color
/// del catálogo no la rompe), por eso la edición tiene que llevar el cambio a esas copias.
/// </summary>
public class ColorAplicarAPrendasTests
{
    private static readonly DateTime Ahora = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ColorRepoFake _colores = new(ColorCatalogo.Crear("verde", "Verde", "#00FF00"));
    private readonly ProductoRepoFake _productos = new();
    private ColorService Svc => new(_colores, _productos);

    private async Task<Producto> Prenda(string id, params (string Nombre, string Hex)[] colores)
    {
        var drafts = colores.Select(c => new ColorDraft(c.Nombre, c.Hex, Atributos.FamiliaDeHex(c.Hex))).ToList();
        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = colores.Select(_ => 3).ToArray() },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = colores.Select(_ => 1).ToArray() },
        };
        var n = 0;
        var p = Producto.Crear(
            id, $"Prenda {id}", null, "vestidos", Silueta.Vestido, 300m, 100m, true,
            drafts, new[] { "M" }, stock, Ahora,
            nuevoIdColor: () => $"{id}-col{++n}", nuevoIdVariante: () => $"{id}-v{n}");
        await _productos.AgregarAsync(p);
        return p;
    }

    private static ColorProducto ColorDe(Producto p, string nombre) => p.Colores.Single(c => c.Nombre == nombre);

    // ── Editar el catálogo cambia las prendas ──

    [Fact]
    public async Task Editar_el_color_lo_cambia_en_las_prendas_que_lo_usan()
    {
        var corset = await Prenda("p1", ("Verde", "#00FF00"));

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));

        Assert.Equal(1, r.PrendasActualizadas);
        var color = ColorDe(corset, "Verde");
        Assert.Equal("#117D11", color.Hex);
        Assert.Equal(FamiliaColor.Verde, color.Familia);
    }

    [Fact]
    public async Task Llega_tambien_a_las_prendas_inactivas_y_cuenta_cada_prenda_una_vez()
    {
        var activa = await Prenda("p1", ("Verde", "#00FF00"));
        var inactiva = await Prenda("p2", ("Verde", "#00FF00"));
        inactiva.Desactivar();

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));

        Assert.Equal(2, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(activa, "Verde").Hex);
        Assert.Equal("#117D11", ColorDe(inactiva, "Verde").Hex);
    }

    [Fact]
    public async Task No_toca_prendas_que_son_de_otro_color()
    {
        var verde = await Prenda("p1", ("Verde", "#00FF00"));
        var rojo = await Prenda("p2", ("Rojo", "#FF0000"));

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));

        Assert.Equal(1, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(verde, "Verde").Hex);
        Assert.Equal("#FF0000", ColorDe(rojo, "Rojo").Hex);
    }

    [Fact]
    public async Task Reconoce_la_copia_sin_importar_mayusculas_del_nombre()
    {
        var p = await Prenda("p1", ("VERDE", "#00ff00"));
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));
        Assert.Equal(1, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(p, "Verde").Hex);
    }

    [Fact]
    public async Task Si_cambia_el_nombre_la_copia_se_renombra_y_las_variantes_siguen_ligadas()
    {
        var p = await Prenda("p1", ("Verde", "#00FF00"));
        var colorId = ColorDe(p, "Verde").Id;
        var variantesAntes = p.Variantes.Select(v => (v.Id, v.ColorId, v.StockTemixco, v.StockAzteca)).ToList();

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde bosque", "#117D11"));

        Assert.Equal(1, r.PrendasActualizadas);
        var color = Assert.Single(p.Colores);
        Assert.Equal(colorId, color.Id);            // es el MISMO color: no se creó otro
        Assert.Equal("Verde bosque", color.Nombre);
        Assert.Equal(variantesAntes, p.Variantes.Select(v => (v.Id, v.ColorId, v.StockTemixco, v.StockAzteca)).ToList());
    }

    [Fact]
    public async Task No_pisa_una_prenda_que_ya_tiene_otro_color_con_el_nombre_nuevo()
    {
        var p = await Prenda("p1", ("Verde", "#00FF00"), ("Verde bosque", "#1F4D2E"));

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde bosque", "#117D11"));

        Assert.Equal(0, r.PrendasActualizadas); // quedarían dos colores con el mismo nombre
        Assert.Equal("#00FF00", ColorDe(p, "Verde").Hex);
        Assert.Equal("#1F4D2E", ColorDe(p, "Verde bosque").Hex);
    }

    [Fact]
    public async Task Sin_prendas_que_lo_usen_no_falla()
    {
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));
        Assert.Equal(0, r.PrendasActualizadas);
        Assert.Equal("#117D11", r.Hex);
    }

    [Fact]
    public async Task Guardar_el_mismo_color_sin_cambios_no_cuenta_prendas_actualizadas()
    {
        await Prenda("p1", ("Verde", "#00FF00"));
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#00FF00"));
        Assert.Equal(0, r.PrendasActualizadas);
    }

    [Fact]
    public async Task Un_nombre_repetido_en_el_catalogo_sigue_rechazandose_y_no_toca_prendas()
    {
        await _colores.AgregarAsync(ColorCatalogo.Crear("rojo", "Rojo", "#FF0000"));
        var p = await Prenda("p1", ("Verde", "#00FF00"));

        var ex = await Assert.ThrowsAsync<DomnaException>(() => Svc.EditarAsync("verde", new ColorInput("Rojo", "#117D11")));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("#00FF00", ColorDe(p, "Verde").Hex);
    }

    [Fact]
    public async Task Editar_un_color_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => Svc.EditarAsync("nada", new ColorInput("X", "#112233")));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Borrar_un_color_del_catalogo_no_rompe_las_prendas_que_lo_usan()
    {
        var p = await Prenda("p1", ("Verde", "#00FF00"));
        await Svc.EliminarAsync("verde");

        Assert.Equal("#00FF00", ColorDe(p, "Verde").Hex); // conserva su copia
    }

    // ── Uso ──

    [Fact]
    public async Task Uso_cuenta_las_prendas_activas_o_no_que_usan_el_color()
    {
        await Prenda("p1", ("Verde", "#00FF00"));
        var inactiva = await Prenda("p2", ("Verde", "#00FF00"));
        inactiva.Desactivar();
        await Prenda("p3", ("Rojo", "#FF0000"));

        Assert.Equal(2, (await Svc.UsoAsync("verde")).Prendas);
    }

    [Fact]
    public async Task Uso_de_un_color_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => Svc.UsoAsync("nada"));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── Ponerse al día al arrancar (prendas que ya quedaron con un tono viejo) ──

    [Fact]
    public async Task Sincronizar_pone_al_dia_las_prendas_que_quedaron_desfasadas()
    {
        // Como el Corset real: el catálogo cambió a #117D11 pero la prenda se quedó con #00FF00.
        var corset = await Prenda("p1", ("Verde", "#00FF00"));
        var estraple = await Prenda("p2", ("Marfil", "#A7D7C5"));
        _colores.Colores.Single(c => c.Id == "verde").Actualizar("Verde", "#117D11");
        await _colores.AgregarAsync(ColorCatalogo.Crear("marfil", "Marfil", "#F5F0E1"));

        var cambios = await Svc.SincronizarPrendasAsync();

        Assert.Equal(2, cambios);
        Assert.Equal("#117D11", ColorDe(corset, "Verde").Hex);
        Assert.Equal("#F5F0E1", ColorDe(estraple, "Marfil").Hex);
        Assert.Equal(FamiliaColor.Blanco, ColorDe(estraple, "Marfil").Familia);
    }

    [Fact]
    public async Task Sincronizar_es_idempotente_la_segunda_vez_no_cambia_nada()
    {
        await Prenda("p1", ("Verde", "#00FF00"));
        _colores.Colores.Single(c => c.Id == "verde").Actualizar("Verde", "#117D11");

        Assert.Equal(1, await Svc.SincronizarPrendasAsync());
        Assert.Equal(0, await Svc.SincronizarPrendasAsync());
    }

    [Fact]
    public async Task Sincronizar_no_toca_colores_que_ya_no_estan_en_el_catalogo()
    {
        var p = await Prenda("p1", ("Coral antiguo", "#FF7F6B"));

        Assert.Equal(0, await Svc.SincronizarPrendasAsync());
        Assert.Equal("#FF7F6B", ColorDe(p, "Coral antiguo").Hex);
    }

    [Fact]
    public async Task Sincronizar_sin_prendas_ni_colores_no_falla()
    {
        Assert.Equal(0, await Svc.SincronizarPrendasAsync());
    }
}
