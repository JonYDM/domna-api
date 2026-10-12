using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Movimientos de inventario con historial: guardar cambios de stock en lote (con motivo) y
/// mover piezas entre sucursales. Nunca deja el stock físico por debajo de lo apartado: esas
/// piezas son de una clienta.
/// </summary>
public class StockService : IStockService
{
    private const int MaxPorVariante = 999;

    private readonly IProductoRepository _productos;
    private readonly IApartadoRepository _apartados;
    private readonly IMovimientoRepository _movimientos;
    private readonly IReloj _reloj;

    public StockService(
        IProductoRepository productos,
        IApartadoRepository apartados,
        IMovimientoRepository movimientos,
        IReloj reloj)
    {
        _productos = productos;
        _apartados = apartados;
        _movimientos = movimientos;
        _reloj = reloj;
    }

    /// <summary>
    /// Todo o nada: primero valida cada cambio (entero 0..999 y no menor a lo apartado) y solo si
    /// todos son válidos los aplica. Cada cambio real deja un renglón en el historial.
    /// </summary>
    /// <returns>Cuántas variantes se validaron (los cambios sin diferencia no dejan historial).</returns>
    public async Task<GuardarStockResultado> GuardarLoteAsync(string productoId, GuardarStockInput input, CancellationToken ct = default)
    {
        var motivo = EnumMap.Motivo(input.Motivo);
        if (motivo == MotivoAjuste.Traspaso)
            throw DomnaException.BadRequest("El traspaso se hace moviendo piezas entre sucursales.");

        var producto = await _productos.ObtenerPorIdAsync(productoId, ct)
            ?? throw DomnaException.NotFound("Producto no encontrado.");
        if (input.Cambios is null || input.Cambios.Count == 0)
            throw DomnaException.BadRequest("No hay cambios que guardar.");

        var repetido = input.Cambios.GroupBy(c => (c.VarianteId, Suc: c.Sucursal?.Trim().ToLowerInvariant())).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null)
            throw DomnaException.BadRequest("Hay dos cambios para la misma talla y sucursal.");

        var reservado = await _apartados.ObtenerReservadoPorVariantesAsync(input.Cambios.Select(c => c.VarianteId).Distinct(), ct);

        // Fase 1: validar todo.
        var validos = new List<(Variante Variante, SucursalId Sucursal, int Nuevo)>();
        foreach (var c in input.Cambios)
        {
            var variante = producto.Variantes.FirstOrDefault(v => v.Id == c.VarianteId)
                ?? throw DomnaException.NotFound("Variante no encontrada.");
            var sucursal = EnumMap.Sucursal(c.Sucursal);
            if (c.Nuevo < 0 || c.Nuevo > MaxPorVariante)
                throw DomnaException.BadRequest("Cantidad inválida.");
            var apartadas = ReservadoDe(reservado, variante.Id, sucursal);
            if (c.Nuevo < apartadas)
                throw DomnaException.Conflict($"Talla {variante.Talla}: no puedes dejar menos de {apartadas} (están apartadas).");
            validos.Add((variante, sucursal, c.Nuevo));
        }

        // Fase 2: aplicar.
        var ahora = _reloj.Ahora;
        foreach (var (variante, sucursal, nuevo) in validos)
        {
            var delta = nuevo - variante.StockDe(sucursal);
            if (delta == 0) continue;
            producto.AjustarStock(variante.Id, sucursal, nuevo, ahora);
            await _movimientos.AgregarAsync(NuevoMovimiento(ahora, producto.Id, variante.Id, sucursal, delta, motivo), ct);
        }

        await _productos.GuardarAsync(ct);
        return new GuardarStockResultado(validos.Count);
    }

    /// <summary>Mueve piezas DISPONIBLES (las apartadas no se mueven) de una sucursal a la otra.</summary>
    public async Task MoverAsync(MoverStockInput input, CancellationToken ct = default)
    {
        var desde = EnumMap.Sucursal(input.Desde);
        var hacia = desde == SucursalId.Temixco ? SucursalId.Azteca : SucursalId.Temixco;

        var producto = await _productos.ObtenerPorVarianteAsync(input.VarianteId, ct)
            ?? throw DomnaException.NotFound("Variante no encontrada.");
        var variante = producto.Variantes.First(v => v.Id == input.VarianteId);

        if (input.Cantidad < 1)
            throw DomnaException.BadRequest("Cantidad inválida.");

        var reservado = await _apartados.ObtenerReservadoPorVariantesAsync(new[] { variante.Id }, ct);
        var libres = variante.StockDe(desde) - ReservadoDe(reservado, variante.Id, desde);
        if (input.Cantidad > libres)
            throw DomnaException.Conflict($"Solo hay {Math.Max(0, libres)} disponibles para mover.");

        var ahora = _reloj.Ahora;
        producto.AjustarStock(variante.Id, desde, variante.StockDe(desde) - input.Cantidad, ahora);
        producto.AjustarStock(variante.Id, hacia, variante.StockDe(hacia) + input.Cantidad, ahora);

        await _movimientos.AgregarAsync(NuevoMovimiento(ahora, producto.Id, variante.Id, desde, -input.Cantidad, MotivoAjuste.Traspaso), ct);
        await _movimientos.AgregarAsync(NuevoMovimiento(ahora, producto.Id, variante.Id, hacia, input.Cantidad, MotivoAjuste.Traspaso), ct);
        await _productos.GuardarAsync(ct);
    }

    private static int ReservadoDe(Dictionary<string, Dictionary<string, int>> reservado, string varianteId, SucursalId sucursal) =>
        reservado.TryGetValue(varianteId, out var porSucursal)
            ? porSucursal.GetValueOrDefault(sucursal == SucursalId.Temixco ? "temixco" : "azteca")
            : 0;

    private static MovimientoInventario NuevoMovimiento(
        DateTime fecha, string productoId, string varianteId, SucursalId sucursal, int delta, MotivoAjuste motivo) =>
        new($"mv-{Guid.NewGuid():N}"[..14], fecha, productoId, varianteId, sucursal, delta, motivo);
}
