using System.Security.Claims;
using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// Apartados y ventas de contado. Una clienta solo cotiza, crea y consulta lo suyo, siempre en el
/// canal "online" (su pago queda por confirmar). El personal (dueña/cajera) vende en mostrador,
/// registra abonos, confirma pagos y administra todo.
/// </summary>
[ApiController]
[Route("api/apartados")]
public class ApartadosController : ControllerBase
{
    private readonly IApartadoService _apartados;
    private readonly IComprobanteService _comprobantes;

    public ApartadosController(IApartadoService apartados, IComprobanteService comprobantes)
    {
        _apartados = apartados;
        _comprobantes = comprobantes;
    }

    private string MiId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "";
    private bool EsStaff => User.IsInRole("duena") || User.IsInRole("cajera");

    /// <summary>
    /// Una clienta no decide por quién ni por qué canal se hace el pedido: siempre es el suyo y en
    /// línea. Si pudiera mandar canal "fisico" se saltaría la confirmación del pago.
    /// </summary>
    private CrearApartadoInput Saneado(CrearApartadoInput input) =>
        EsStaff ? input : input with { Canal = "online", ClientaId = MiId };

    /// <summary>Cotiza sin reservar nada (lo que ve la clienta/cajera antes de confirmar).</summary>
    [HttpPost("cotizar")]
    [Authorize]
    public async Task<ActionResult<CotizacionApartadoDto>> Cotizar(CrearApartadoInput input, CancellationToken ct)
    {
        var i = Saneado(input);
        return Ok(await _apartados.CotizarAsync(i.ClientaId, i, ct));
    }

    /// <summary>Crea el apartado o, si la modalidad es "compra", la venta directa (POS incluido).</summary>
    [HttpPost]
    [Authorize]
    [Idempotente]
    public async Task<ActionResult<ApartadoDto>> Crear(CrearApartadoInput input, CancellationToken ct)
    {
        var i = Saneado(input);
        return Ok(await _apartados.CrearAsync(i.ClientaId, i, ct));
    }

    /// <summary>Lista de la dueña con filtros (estado, texto, tipo apartados|compras).</summary>
    [HttpGet]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<List<ApartadoDto>>> Listar(
        [FromQuery] string? estado, [FromQuery] string? texto, [FromQuery] string? tipo, CancellationToken ct)
        => Ok(await _apartados.ListarAsync(estado, texto, tipo, ct));

    /// <summary>Pedidos con pago por confirmar. La cajera ve los que se entregan en su sucursal; la dueña, todos.</summary>
    /// <summary>
    /// Pedidos de a poco: filtra y busca en la base de datos y devuelve una página con el total. `pagina`
    /// empieza en 1 y `tamano` se acota a 50. Es lo que usa la pantalla de Pedidos.
    /// </summary>
    [HttpGet("pagina")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<PaginaDto<ApartadoDto>>> Pagina(
        [FromQuery] string? estado, [FromQuery] string? texto, [FromQuery] string? tipo,
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, CancellationToken ct = default)
        => Ok(await _apartados.BuscarAsync(estado, texto, tipo, pagina, tamano, ct));

    /// <summary>Los números de las pestañas de Pedidos (apartados activos y compras por entregar).</summary>
    [HttpGet("conteos")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ConteosPedidosDto>> Conteos(CancellationToken ct)
        => Ok(await _apartados.ConteosAsync(ct));

    [HttpGet("por-confirmar")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<List<ApartadoDto>>> PorConfirmar(CancellationToken ct)
        => Ok(await _apartados.ListarPorConfirmarAsync(MiId, ct));

    /// <summary>Detalle de un apartado. Una clienta solo ve los suyos.</summary>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApartadoDto>> Obtener(string id, CancellationToken ct)
    {
        var a = await _apartados.ObtenerAsync(id, ct);
        // Para no revelar que existe, un pedido ajeno responde igual que uno inexistente.
        if (!EsStaff && a.ClientaId != MiId) throw DomnaException.NotFound("Apartado no encontrado.");
        return Ok(a);
    }

    /// <summary>Pedidos de una clienta (para "Mis pedidos"). Una clienta solo consulta los suyos.</summary>
    [HttpGet("clienta/{clientaId}")]
    [Authorize]
    public async Task<ActionResult<List<ApartadoDto>>> ListarDeClienta(string clientaId, CancellationToken ct)
    {
        if (!EsStaff && clientaId != MiId) return Forbid();
        return Ok(await _apartados.ListarDeClientaAsync(clientaId, ct));
    }

    /// <summary>Registra un abono cobrado en mostrador; si cubre el saldo, liquida y descuenta el stock físico.</summary>
    [HttpPost("abonos")]
    [Authorize(Roles = "duena,cajera")]
    [Idempotente]
    public async Task<ActionResult<ApartadoDto>> Abonar(RegistrarAbonoInput input, CancellationToken ct)
        => Ok(await _apartados.AbonarAsync(input, ct));

    /// <summary>La tienda verificó el pago declarado en línea: pasa a pagado (y liquida si cubre el saldo).</summary>
    [HttpPost("{id}/abonos/{abonoId}/confirmar")]
    [Authorize(Roles = "duena,cajera")]
    [Idempotente]
    public async Task<ActionResult<ApartadoDto>> ConfirmarPago(string id, string abonoId, CancellationToken ct)
        => Ok(await _apartados.ConfirmarPagoAsync(id, abonoId, MiId, ct));

    /// <summary>La tienda no encontró el pago. Queda el motivo; el pedido sigue activo hasta que venza.</summary>
    [HttpPost("{id}/abonos/{abonoId}/rechazar")]
    [Authorize(Roles = "duena,cajera")]
    [Idempotente]
    public async Task<ActionResult<ApartadoDto>> RechazarPago(string id, string abonoId, RechazarPagoInput input, CancellationToken ct)
        => Ok(await _apartados.RechazarPagoAsync(id, abonoId, input.Motivo, MiId, ct));

    /// <summary>La clienta pide dónde subir el comprobante de su transferencia (carpeta privada).</summary>
    [HttpPost("{id}/comprobante/firmar")]
    [Authorize]
    public async Task<ActionResult<ComprobanteFirmadoDto>> FirmarComprobante(string id, SubirComprobanteInput input, CancellationToken ct)
        => Ok(await _comprobantes.FirmarSubidaAsync(id, input.ContentType, MiId, ct));

    /// <summary>La clienta registra el comprobante ya subido; el pago queda en revisión.</summary>
    [HttpPost("{id}/comprobante")]
    [Authorize]
    [Idempotente]
    public async Task<ActionResult<ApartadoDto>> RegistrarComprobante(string id, RegistrarComprobanteInput input, CancellationToken ct)
        => Ok(await _comprobantes.RegistrarAsync(id, input.Clave, MiId, ct));

    /// <summary>El personal abre un comprobante: pide su PIN y devuelve un enlace que dura 2 minutos.</summary>
    [HttpPost("{id}/abonos/{abonoId}/comprobante/ver")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ComprobanteVistaDto>> VerComprobante(string id, string abonoId, VerComprobanteInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await _comprobantes.VerAsync(id, abonoId, input.Pin, MiId, ct));
    }

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
