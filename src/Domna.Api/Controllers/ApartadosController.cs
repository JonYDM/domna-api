using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// Apartados y ventas de contado. Cotizar/crear es público si no hay clientaId (público general,
/// POS) o requiere la propia sesión de la clienta; el resto (listar/abonar/cancelar/avanzar) es staff.
/// </summary>
[ApiController]
[Route("api/apartados")]
public class ApartadosController : ControllerBase
{
    private readonly IApartadoService _apartados;

    public ApartadosController(IApartadoService apartados) => _apartados = apartados;

    /// <summary>Cotiza sin reservar nada (lo que ve la clienta/cajera antes de confirmar).</summary>
    [HttpPost("cotizar")]
    [Authorize]
    public async Task<ActionResult<CotizacionApartadoDto>> Cotizar(CrearApartadoInput input, CancellationToken ct)
        => Ok(await _apartados.CotizarAsync(input.ClientaId, input, ct));

    /// <summary>Crea el apartado o, si la modalidad es "compra", la venta directa (POS incluido).</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApartadoDto>> Crear(CrearApartadoInput input, CancellationToken ct)
        => Ok(await _apartados.CrearAsync(input.ClientaId, input, ct));

    /// <summary>Lista de la dueña con filtros (estado, texto, tipo apartados|compras).</summary>
    [HttpGet]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<List<ApartadoDto>>> Listar(
        [FromQuery] string? estado, [FromQuery] string? texto, [FromQuery] string? tipo, CancellationToken ct)
        => Ok(await _apartados.ListarAsync(estado, texto, tipo, ct));

    /// <summary>Detalle de un apartado.</summary>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApartadoDto>> Obtener(string id, CancellationToken ct)
        => Ok(await _apartados.ObtenerAsync(id, ct));

    /// <summary>Pedidos de una clienta (para "Mis pedidos").</summary>
    [HttpGet("clienta/{clientaId}")]
    [Authorize]
    public async Task<ActionResult<List<ApartadoDto>>> ListarDeClienta(string clientaId, CancellationToken ct)
        => Ok(await _apartados.ListarDeClientaAsync(clientaId, ct));

    /// <summary>Registra un abono; si cubre el saldo, liquida y descuenta el stock físico.</summary>
    [HttpPost("abonos")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ApartadoDto>> Abonar(RegistrarAbonoInput input, CancellationToken ct)
        => Ok(await _apartados.AbonarAsync(input, ct));

    /// <summary>Cancela un apartado activo (libera la reserva de stock).</summary>
    [HttpPost("{id}/cancelar")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ApartadoDto>> Cancelar(string id, CancellationToken ct)
        => Ok(await _apartados.CancelarAsync(id, ct));

    /// <summary>Avanza el estado de entrega (en_origen → en_traslado → listo → entregado).</summary>
    [HttpPost("{id}/entrega")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ApartadoDto>> AvanzarEntrega(string id, AvanzarEntregaInput input, CancellationToken ct)
        => Ok(await _apartados.AvanzarEntregaAsync(id, input.Siguiente, ct));

    /// <summary>Corte del día de una sucursal (lo que ve la cajera).</summary>
    [HttpGet("corte/{sucursal}")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<CorteDiaDto>> CorteDia(string sucursal, CancellationToken ct)
        => Ok(await _apartados.CorteDiaAsync(sucursal, ct));

    /// <summary>Métricas del dashboard de la dueña (opcionalmente filtradas por sucursal).</summary>
    [HttpGet("metricas")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<MetricasDto>> Metricas([FromQuery] string? sucursal, CancellationToken ct)
        => Ok(await _apartados.MetricasAsync(sucursal, ct));
}
