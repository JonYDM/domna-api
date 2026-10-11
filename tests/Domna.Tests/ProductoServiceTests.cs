using Domna.Application.Dtos;
using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class ProductoServiceTests
{
    private readonly RelojFijo _reloj = new();

    /// <summary>Categoría "vestidos" con tallas CH/M/G/XG (cubre las tallas usadas en las pruebas).</summary>
    private CategoriaRepoFake Catalogo() =>
        new(Categoria.Crear("vestidos", "Vestidos", new[] { "CH", "M", "G", "XG" }));

    private ProductoService Crear(ProductoRepoFake repo, CategoriaRepoFake? categorias = null)
        => new(repo, categorias ?? Catalogo(), _reloj);

    private static NuevoProductoInput NuevoVestido() => new(
        Nombre: "Vestido Primavera",
        Descripcion: "Ligero",
        CategoriaId: "vestidos",
        Silueta: "vestido",
        Precio: 599m,
        Costo: 280m,
        Colores: new List<ColorInputDto>
        {
            new("Rosa", "#F2A7B5", "rosa"),
            new("Azul", "#4A6A92", "azul")
        },
        Tallas: new List<string> { "CH", "M" },
        Stock: new Dictionary<string, Dictionary<string, int[]>>
        {
            ["temixco"] = new() { ["CH"] = new[] { 3, 2 }, ["M"] = new[] { 1, 0 } },
            ["azteca"] = new() { ["CH"] = new[] { 0, 1 }, ["M"] = new[] { 2, 2 } }
        },
        PermiteApartado: true,
        CodigoBarras: new Dictionary<string, string?[]>
        {
            ["CH"] = new string?[] { "7501000000011", "7501000000028" },
            ["M"] = new string?[] { "7501000000035", "7501000000042" }
        });

    [Fact]
    public async Task Crear_devuelve_producto_con_variantes_y_stock()
    {
        var repo = new ProductoRepoFake();
        var svc = Crear(repo);

        var dto = await svc.CrearAsync(NuevoVestido());

        Assert.Equal(4, dto.Variantes.Count); // 2 tallas × 2 colores
        Assert.Equal(2, dto.Colores.Count);
        Assert.Equal("vestido", dto.Silueta);
        var chRosa = dto.Variantes.First(v => v.CodigoBarras == "7501000000011");
        Assert.Equal(3, chRosa.Stock["temixco"]);
        Assert.Equal(0, chRosa.Stock["azteca"]);
    }

    [Fact]
    public async Task Listar_oculta_inactivos_por_defecto()
    {
        var repo = new ProductoRepoFake();
        var svc = Crear(repo);
        var dto = await svc.CrearAsync(NuevoVestido());
        await svc.DesactivarAsync(dto.Id);

        Assert.Empty(await svc.ListarAsync(incluirInactivos: false));
        Assert.Single(await svc.ListarAsync(incluirInactivos: true));
    }

    [Fact]
    public async Task Obtener_inexistente_lanza_404()
    {
        var svc = Crear(new ProductoRepoFake());
        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.ObtenerAsync("no-existe"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task BuscarPorCodigoBarras_encuentra_la_prenda()
    {
        var repo = new ProductoRepoFake();
        var svc = Crear(repo);
        await svc.CrearAsync(NuevoVestido());

        var dto = await svc.BuscarPorCodigoBarrasAsync("7501000000042");
        Assert.Equal("Vestido Primavera", dto.Nombre);
    }

    [Fact]
    public async Task BuscarPorCodigoBarras_inexistente_lanza_404()
    {
        var repo = new ProductoRepoFake();
        var svc = Crear(repo);
        await svc.CrearAsync(NuevoVestido());

        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.BuscarPorCodigoBarrasAsync("000"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Editar_cambia_campos_simples()
    {
        var repo = new ProductoRepoFake();
        var svc = Crear(repo);
        var dto = await svc.CrearAsync(NuevoVestido());

        var editado = await svc.EditarAsync(dto.Id,
            new EditarProductoInput("Vestido Verano", "Nuevo", 650m, 300m, 799m, false));

        Assert.Equal("Vestido Verano", editado.Nombre);
        Assert.Equal(650m, editado.Precio);
        Assert.Equal(799m, editado.PrecioAntes);
        Assert.False(editado.PermiteApartado);
    }

    [Fact]
    public async Task AjustarStock_actualiza_la_variante()
    {
        var repo = new ProductoRepoFake();
        var svc = Crear(repo);
        var dto = await svc.CrearAsync(NuevoVestido());
        var variante = dto.Variantes.First();

        var actualizado = await svc.AjustarStockAsync(dto.Id,
            new AjusteStockInput(variante.Id, "temixco", 10));

        var v = actualizado.Variantes.First(x => x.Id == variante.Id);
        Assert.Equal(10, v.Stock["temixco"]);
    }

    [Fact]
    public async Task Crear_rechaza_categoria_inexistente()
    {
        var repo = new ProductoRepoFake();
        // Catálogo vacío: la categoría "vestidos" no existe.
        var svc = Crear(repo, new CategoriaRepoFake());

        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.CrearAsync(NuevoVestido()));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Crear_rechaza_talla_ajena_a_la_categoria()
    {
        var repo = new ProductoRepoFake();
        // Categoría solo con CH: la talla "M" del input es ajena.
        var soloCh = new CategoriaRepoFake(Categoria.Crear("vestidos", "Vestidos", new[] { "CH" }));
        var svc = Crear(repo, soloCh);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.CrearAsync(NuevoVestido()));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("M", ex.Message);
    }
}
