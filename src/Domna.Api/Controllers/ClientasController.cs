using System.Security.Claims;
using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>Panel de Clientas de la dueña: lista con filtros, resumen y ficha.</summary>
[ApiController]
[Route("api/clientas")]
public class ClientasController : ControllerBase
{
    private readonly IClientasPanelService _clientas;

    public ClientasController(IClientasPanelService clientas) => _clientas = clientas;

    /// <summary>Lista clientas con filtro (todas|activas|nuevas|cargo) y búsqueda por nombre/correo/teléfono.</summary>
    [HttpGet]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<List<ClientaResumenDto>>> Listar(
        [FromQuery] string filtro, [FromQuery] string? texto, CancellationToken ct)
        => Ok(await _clientas.ListarAsync(filtro ?? "todas", texto, ct));

    /// <summary>Totales del panel: cuántas clientas, nuevas de la semana, con cargo, cómo llegaron.</summary>
    [HttpGet("resumen")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ResumenClientasDto>> Resumen(CancellationToken ct)
        => Ok(await _clientas.ResumenAsync(ct));

    /// <summary>Ficha de una clienta (panel de la dueña).</summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ClientaResumenDto>> Obtener(string id, CancellationToken ct)
        => Ok(await _clientas.ObtenerAsync(id, ct));

    /// <summary>
    /// Datos básicos de la PROPIA clienta (para su perfil en la tienda). Solo puede verse a sí
    /// misma; la dueña también puede (por si abre la tienda como preview).
    /// </summary>
    [HttpGet("{id}/propio")]
    [Authorize]
    public async Task<ActionResult<ClientaResumenDto>> ObtenerPropio(string id, CancellationToken ct)
    {
        var miId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var esDuena = User.IsInRole("duena");
        if (!esDuena && miId != id)
            return Forbid();
        return Ok(await _clientas.ObtenerAsync(id, ct));
    }

    /// <summary>Alta en mostrador: nace sin PIN; si se registra con el mismo teléfono, reclama la ficha.</summary>
    [HttpPost]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ClientaDto>> Crear(CrearClientaInput input, CancellationToken ct)
        => Ok(await _clientas.CrearAsync(input, ct));
}
