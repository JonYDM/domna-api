using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Editar un color del catálogo y, si se pide, llevar el cambio a las prendas que lo usan. Cada
/// prenda guarda su PROPIA copia del color, así que sin esto editar el catálogo no las tocaba.
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

    // ── Aplicar a prendas ──

    [Fact]
    public async Task Sin_aplicar_las_prendas_no_cambian_aunque_el_catalogo_si()
    {
        var corset = await Prenda("p1", ("Verde", "#00FF00"));

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));

        Assert.Equal("#117D11", r.Hex);
        Assert.Equal(0, r.PrendasActualizadas);
        Assert.Equal("#00FF00", ColorDe(corset, "Verde").Hex); // la copia de la prenda sigue igual
    }

    [Fact]
    public async Task Aplicando_las_prendas_que_lo_usan_toman_el_color_nuevo()
    {
        var corset = await Prenda("p1", ("Verde", "#00FF00"));

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", AplicarAPrendas: true));

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

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", true));

        Assert.Equal(2, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(activa, "Verde").Hex);
        Assert.Equal("#117D11", ColorDe(inactiva, "Verde").Hex);
    }

    [Fact]
    public async Task Pone_al_dia_tambien_las_copias_que_ya_quedaron_con_otro_tono()
    {
        // El caso real: el catalogo se edito (sin aplicar) y la prenda quedo con el tono viejo.
        var corset = await Prenda("p1", ("Verde", "#00FF00"));
        await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11")); // solo catalogo
        Assert.Equal("#00FF00", ColorDe(corset, "Verde").Hex);

        // Guardar el MISMO color pidiendo aplicar basta para ponerla al dia.
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", AplicarAPrendas: true));

        Assert.Equal(1, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(corset, "Verde").Hex);
    }

    [Fact]
    public async Task No_toca_prendas_que_son_de_otro_color()
    {
        var verde = await Prenda("p1", ("Verde", "#00FF00"));
        var rojo = await Prenda("p2", ("Rojo", "#FF0000"));

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", true));

        Assert.Equal(1, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(verde, "Verde").Hex);
        Assert.Equal("#FF0000", ColorDe(rojo, "Rojo").Hex);
    }

    [Fact]
    public async Task Si_ya_estan_al_dia_no_cuenta_prendas_actualizadas()
    {
        await Prenda("p1", ("Verde", "#00FF00"));
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#00FF00", true)); // mismo color
        Assert.Equal(0, r.PrendasActualizadas);
    }

    [Fact]
    public async Task Reconoce_la_copia_sin_importar_mayusculas_del_nombre()
    {
        var p = await Prenda("p1", ("VERDE", "#00ff00"));
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", true));
        Assert.Equal(1, r.PrendasActualizadas);
        Assert.Equal("#117D11", ColorDe(p, "Verde").Hex);
    }

    [Fact]
    public async Task Si_cambia_el_nombre_la_copia_se_renombra_y_las_variantes_siguen_ligadas()
    {
        var p = await Prenda("p1", ("Verde", "#00FF00"));
        var colorId = ColorDe(p, "Verde").Id;
        var variantesAntes = p.Variantes.Select(v => (v.Id, v.ColorId, v.StockTemixco, v.StockAzteca)).ToList();

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde bosque", "#117D11", true));

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

        var r = await Svc.EditarAsync("verde", new ColorInput("Verde bosque", "#117D11", true));

        Assert.Equal(0, r.PrendasActualizadas); // quedarían dos colores con el mismo nombre
        Assert.Equal("#00FF00", ColorDe(p, "Verde").Hex);
        Assert.Equal("#1F4D2E", ColorDe(p, "Verde bosque").Hex);
    }

    [Fact]
    public async Task Sin_prendas_que_lo_usen_no_falla()
    {
        var r = await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", true));
        Assert.Equal(0, r.PrendasActualizadas);
        Assert.Equal("#117D11", r.Hex);
    }

    [Fact]
    public async Task Un_nombre_repetido_en_el_catalogo_sigue_rechazandose_y_no_toca_prendas()
    {
        await _colores.AgregarAsync(ColorCatalogo.Crear("rojo", "Rojo", "#FF0000"));
        var p = await Prenda("p1", ("Verde", "#00FF00"));

        var ex = await Assert.ThrowsAsync<DomnaException>(() => Svc.EditarAsync("verde", new ColorInput("Rojo", "#117D11", true)));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("#00FF00", ColorDe(p, "Verde").Hex);
    }

    [Fact]
    public async Task Editar_un_color_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => Svc.EditarAsync("nada", new ColorInput("X", "#112233", true)));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── Uso ──

    [Fact]
    public async Task Uso_cuenta_las_prendas_del_color_y_cuantas_lo_muestran_distinto()
    {
        await Prenda("p1", ("Verde", "#00FF00"));                 // al dia
        var inactiva = await Prenda("p2", ("Verde", "#00FF00"));  // al dia (inactiva tambien cuenta)
        inactiva.Desactivar();
        await Prenda("p3", ("Verde", "#123456"));                 // con otro tono
        await Prenda("p4", ("Rojo", "#FF0000"));                  // de otro color

        var uso = await Svc.UsoAsync("verde");

        Assert.Equal(3, uso.Prendas);
        Assert.Equal(1, uso.Desactualizadas);
    }

    [Fact]
    public async Task Uso_tras_editar_el_catalogo_sin_aplicar_marca_las_prendas_como_desactualizadas()
    {
        await Prenda("p1", ("Verde", "#00FF00"));
        await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11"));

        var uso = await Svc.UsoAsync("verde");
        Assert.Equal(1, uso.Prendas);
        Assert.Equal(1, uso.Desactualizadas);
    }

    [Fact]
    public async Task Uso_tras_aplicar_deja_todo_al_dia()
    {
        await Prenda("p1", ("Verde", "#00FF00"));
        await Svc.EditarAsync("verde", new ColorInput("Verde", "#117D11", true));

        var uso = await Svc.UsoAsync("verde");
        Assert.Equal(1, uso.Prendas);
        Assert.Equal(0, uso.Desactualizadas);
    }

    [Fact]
    public async Task Uso_de_un_color_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => Svc.UsoAsync("nada"));
        Assert.Equal(404, ex.StatusCode);
    }
}
