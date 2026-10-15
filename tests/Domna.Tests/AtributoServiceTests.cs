using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class CategoriaServiceTests
{
    private CategoriaService Crear(CategoriaRepoFake cats, ProductoRepoFake prods) => new(cats, prods);

    [Fact]
    public async Task Crear_normaliza_tallas_y_asigna_id_por_slug()
    {
        var svc = Crear(new CategoriaRepoFake(), new ProductoRepoFake());
        var dto = await svc.CrearAsync(new CategoriaInput("Vestidos", new List<string> { "ch", "m", "g" }));

        Assert.Equal("vestidos", dto.Id);
        Assert.Equal(new[] { "CH", "M", "G" }, dto.Tallas);
        Assert.Equal(0, dto.Productos);
    }

    [Fact]
    public async Task Crear_rechaza_nombre_duplicado()
    {
        var repo = new CategoriaRepoFake(Categoria.Crear("vestidos", "Vestidos", new[] { "M" }));
        var svc = Crear(repo, new ProductoRepoFake());

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => svc.CrearAsync(new CategoriaInput("  vestidos ", new List<string> { "M" })));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Editar_cambia_nombre_y_tallas()
    {
        var repo = new CategoriaRepoFake(Categoria.Crear("vestidos", "Vestidos", new[] { "M" }));
        var svc = Crear(repo, new ProductoRepoFake());

        var dto = await svc.EditarAsync("vestidos", new CategoriaInput("Vestidos largos", new List<string> { "CH", "M", "G" }));
        Assert.Equal("Vestidos largos", dto.Nombre);
        Assert.Equal(new[] { "CH", "M", "G" }, dto.Tallas);
    }

    [Fact]
    public async Task Eliminar_sin_prendas_funciona()
    {
        var repo = new CategoriaRepoFake(Categoria.Crear("bolsas", "Bolsas", new[] { "Única" }));
        var svc = Crear(repo, new ProductoRepoFake());

        await svc.EliminarAsync("bolsas");
        Assert.Empty(await svc.ListarAsync());
    }

    [Fact]
    public async Task Eliminar_con_prendas_lanza_409()
    {
        var cats = new CategoriaRepoFake(Categoria.Crear("vestidos", "Vestidos", new[] { "M" }));
        var prods = new ProductoRepoFake();
        // Una prenda en la categoría "vestidos".
        await prods.AgregarAsync(Producto.Crear(
            "p1", "Vestido", "d", "vestidos", Silueta.Vestido, 100m, 50m, true,
            new[] { new ColorDraft("Rojo", "#C0283E", FamiliaColor.Rojo) },
            new[] { "M" },
            new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
            {
                [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { 1 } }
            },
            DateTime.UtcNow));
        var svc = Crear(cats, prods);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.EliminarAsync("vestidos"));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Listar_incluye_conteo_de_prendas()
    {
        var cats = new CategoriaRepoFake(Categoria.Crear("vestidos", "Vestidos", new[] { "M" }));
        var prods = new ProductoRepoFake();
        await prods.AgregarAsync(Producto.Crear(
            "p1", "Vestido", "d", "vestidos", Silueta.Vestido, 100m, 50m, true,
            new[] { new ColorDraft("Rojo", "#C0283E", FamiliaColor.Rojo) },
            new[] { "M" },
            new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
            {
                [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { 1 } }
            },
            DateTime.UtcNow));
        var svc = Crear(cats, prods);

        var lista = await svc.ListarAsync();
        Assert.Equal(1, lista.Single().Productos);
    }
}

public class ColorServiceTests
{
    private ColorService Crear(ColorRepoFake repo, ProductoRepoFake? productos = null) => new(repo, productos ?? new ProductoRepoFake());

    [Fact]
    public async Task Crear_normaliza_hex_y_calcula_familia()
    {
        var svc = Crear(new ColorRepoFake());
        var dto = await svc.CrearAsync(new ColorInput("Rosa palo", "#f2a7b5"));

        Assert.Equal("rosa-palo", dto.Id);
        Assert.Equal("#F2A7B5", dto.Hex);
        Assert.Equal("rosa", dto.Familia);
    }

    [Fact]
    public async Task Crear_expande_hex_corto()
    {
        var svc = Crear(new ColorRepoFake());
        var dto = await svc.CrearAsync(new ColorInput("Negro", "#111"));
        Assert.Equal("#111111", dto.Hex);
        Assert.Equal("negro", dto.Familia);
    }

    [Fact]
    public async Task Crear_rechaza_nombre_duplicado()
    {
        var repo = new ColorRepoFake(ColorCatalogo.Crear("rosa", "Rosa", "#F2A7B5"));
        var svc = Crear(repo);

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => svc.CrearAsync(new ColorInput("rosa", "#FF0000")));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Crear_rechaza_hex_invalido()
    {
        var svc = Crear(new ColorRepoFake());
        await Assert.ThrowsAsync<DomnaException>(() => svc.CrearAsync(new ColorInput("Raro", "no-es-hex")));
    }

    [Fact]
    public async Task Editar_recalcula_familia()
    {
        var repo = new ColorRepoFake(ColorCatalogo.Crear("color", "Color", "#F2A7B5"));
        var svc = Crear(repo);

        var dto = await svc.EditarAsync("color", new ColorInput("Color", "#1F1B24"));
        Assert.Equal("negro", dto.Familia);
    }

    [Fact]
    public async Task Eliminar_quita_del_catalogo()
    {
        var repo = new ColorRepoFake(ColorCatalogo.Crear("rosa", "Rosa", "#F2A7B5"));
        var svc = Crear(repo);

        await svc.EliminarAsync("rosa");
        Assert.Empty(await svc.ListarAsync());
    }

    [Fact]
    public async Task Eliminar_inexistente_lanza_404()
    {
        var svc = Crear(new ColorRepoFake());
        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.EliminarAsync("no-existe"));
        Assert.Equal(404, ex.StatusCode);
    }
}
