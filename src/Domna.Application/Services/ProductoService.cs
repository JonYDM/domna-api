using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// CRUD de prenda. El alta genera variantes talla × color con stock por sucursal y código de barras
/// por variante (NuevoProductoInput de api.ts). La baja es lógica (desactivar) para conservar historial.
/// </summary>
public class ProductoService : IProductoService
{
    private readonly IProductoRepository _productos;
    private readonly ICategoriaRepository _categorias;
    private readonly IReloj _reloj;

    public ProductoService(IProductoRepository productos, ICategoriaRepository categorias, IReloj reloj)
    {
        _productos = productos;
        _categorias = categorias;
        _reloj = reloj;
    }

    public async Task<List<ProductoDto>> ListarAsync(bool incluirInactivos, CancellationToken ct = default)
    {
        var lista = await _productos.ListarAsync(incluirInactivos, ct);
        return lista.Select(p => p.ToDto()).ToList();
    }

    public async Task<ProductoDto> ObtenerAsync(string id, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        return p.ToDto();
    }

    public async Task<ProductoDto> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw DomnaException.BadRequest("Escanea o escribe un código.");
        var p = await _productos.BuscarPorCodigoBarrasAsync(codigo.Trim(), ct)
            ?? throw DomnaException.NotFound("Ninguna prenda con ese código de barras.");
        return p.ToDto();
    }

    public async Task<ProductoDto> CrearAsync(NuevoProductoInput input, CancellationToken ct = default)
    {
        // La categoría debe existir en el catálogo.
        var categoria = await _categorias.ObtenerPorIdAsync(input.CategoriaId, ct)
            ?? throw DomnaException.BadRequest("La categoría no existe. Créala en el catálogo primero.");

        // Las tallas deben pertenecer a la categoría (se normalizan igual para comparar).
        var tallasNorm = Atributos.NormalizarTallas(input.Tallas);
        var ajenas = tallasNorm.Where(t => !categoria.TieneTalla(t)).ToList();
        if (ajenas.Count > 0)
            throw DomnaException.BadRequest(
                $"Estas tallas no pertenecen a la categoría «{categoria.Nombre}»: {string.Join(", ", ajenas)}.");

        var colores = input.Colores
            .Select(c => new ColorDraft(c.Nombre, c.Hex, EnumMap.Familia(c.Familia)))
            .ToList();

        // Convierte stock[sucursalStr][talla][] → stock[SucursalId][talla][].
        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>();
        foreach (var (sucStr, porTalla) in input.Stock)
            stock[EnumMap.Sucursal(sucStr)] = porTalla;

        var codigos = input.CodigoBarras is null
            ? null
            : (IReadOnlyDictionary<string, string?[]>)input.CodigoBarras;

        var producto = Producto.Crear(
            NuevoId(),
            input.Nombre,
            input.Descripcion,
            input.CategoriaId,
            EnumMap.Silueta(input.Silueta),
            input.Precio,
            input.Costo,
            input.PermiteApartado,
            colores,
            tallasNorm,
            stock,
            _reloj.Ahora,
            codigos);

        await _productos.AgregarAsync(producto, ct);
        await _productos.GuardarAsync(ct);
        return producto.ToDto();
    }

    public async Task<ProductoDto> EditarAsync(string id, EditarProductoInput input, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");

        p.Renombrar(input.Nombre);
        p.CambiarDescripcion(input.Descripcion);
        p.CambiarPrecio(input.Precio);
        p.CambiarCosto(input.Costo);
        p.CambiarPrecioAntes(input.PrecioAntes);
        p.CambiarPermiteApartado(input.PermiteApartado);

        await _productos.GuardarAsync(ct);
        return p.ToDto();
    }

    public async Task<ProductoDto> AjustarStockAsync(string id, AjusteStockInput input, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");

        p.AjustarStock(input.VarianteId, EnumMap.Sucursal(input.Sucursal), input.Nuevo, _reloj.Ahora);
        await _productos.GuardarAsync(ct);
        return p.ToDto();
    }

    public async Task DesactivarAsync(string id, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        p.Desactivar();
        await _productos.GuardarAsync(ct);
    }

    private static string NuevoId() => $"p-{Guid.NewGuid():N}"[..12];
}
