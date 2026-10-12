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

        // Fotos (URLs públicas ya subidas a R2 por el front). Se ligan al color por su índice en
        // la lista de colores del input (mismo orden con el que el producto creó sus colores).
        if (input.Imagenes is { Count: > 0 })
        {
            var coloresProducto = producto.Colores; // mismo orden que input.Colores
            producto.FijarImagenes(
                input.Imagenes
                    .OrderBy(i => i.Orden)
                    .Select(i =>
                    {
                        var colorId = i.ColorIndex is int idx && idx >= 0 && idx < coloresProducto.Count
                            ? coloresProducto[idx].Id
                            : (string?)null;
                        return (i.Url, colorId);
                    }));
        }

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

    public async Task<ProductoDto> AgregarColorAsync(string id, AgregarColorInput input, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");

        // Las tallas deben pertenecer a la categoría del producto.
        var categoria = await _categorias.ObtenerPorIdAsync(p.CategoriaId, ct);
        var tallasNorm = Atributos.NormalizarTallas(input.Tallas);
        if (categoria is not null)
        {
            var ajenas = tallasNorm.Where(t => !categoria.TieneTalla(t)).ToList();
            if (ajenas.Count > 0)
                throw DomnaException.BadRequest(
                    $"Estas tallas no pertenecen a la categoría «{categoria.Nombre}»: {string.Join(", ", ajenas)}.");
        }

        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>();
        foreach (var (sucStr, porTalla) in input.Stock)
            stock[EnumMap.Sucursal(sucStr)] = porTalla;

        p.AgregarColor(
            new ColorDraft(input.Color.Nombre, input.Color.Hex, EnumMap.Familia(input.Color.Familia)),
            tallasNorm,
            stock,
            input.Fotos ?? new List<string>(),
            _reloj.Ahora);

        await _productos.GuardarAsync(ct);
        return p.ToDto();
    }

    public async Task<ProductoDto> QuitarImagenAsync(string id, string url, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        p.QuitarImagen(url);
        await _productos.GuardarAsync(ct);
        return p.ToDto();
    }

    public async Task<ProductoDto> AgregarImagenesColorAsync(string id, string colorId, List<string> urls, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        p.AgregarImagenesAColor(colorId, urls);
        await _productos.GuardarAsync(ct);
        return p.ToDto();
    }

    public async Task<ProductoDto> QuitarColorAsync(string id, string colorId, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        // TODO(apartados): cuando exista el módulo de apartados en el backend, calcular aquí si el
        // color tiene apartados activos y pasarlo. Hoy el backend no gestiona apartados ⇒ false.
        p.QuitarColor(colorId, tieneApartados: false);
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

    public async Task ActivarAsync(string id, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        p.Activar();
        await _productos.GuardarAsync(ct);
    }

    /// <summary>
    /// Borrado físico permanente (irreversible): solo si está inactiva (baja lógica previa) y sin
    /// stock físico en ninguna sucursal. El módulo de apartados/ventas aún no existe en el backend
    /// (TODO: cuando exista, validar aquí también que no tenga apartados ni entregas pendientes).
    /// </summary>
    public async Task EliminarPermanenteAsync(string id, CancellationToken ct = default)
    {
        var p = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Prenda no encontrada.");
        if (p.Activo)
            throw DomnaException.Conflict("Primero oculta la prenda de la tienda antes de eliminarla.");
        var stockFisico = p.Variantes.Sum(v => v.StockTotal);
        if (stockFisico > 0)
            throw DomnaException.Conflict($"Todavía tiene {stockFisico} piezas en inventario. Pon el stock en cero antes de eliminarla.");

        await _productos.EliminarAsync(p, ct);
        await _productos.GuardarAsync(ct);
    }

    private static string NuevoId() => $"p-{Guid.NewGuid():N}"[..12];
}
