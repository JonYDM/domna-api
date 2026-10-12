using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>Panel de Clientas de la dueña: lista con filtros, resumen y ficha.</summary>
[ApiController]
[Route("api/clientas")]
[Authorize(Roles = "duena")]
public class ClientasController : ControllerBase
{
    private readonly IClientasPanelService _clientas;

    public ClientasController(IClientasPanelService clientas) => _clientas = clientas;

    /// <summary>Lista clientas con filtro (todas|activas|nuevas|cargo) y búsqueda por nombre/correo/teléfono.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ClientaResumenDto>>> Listar(
        [FromQuery] string filtro, [FromQuery] string? texto, CancellationToken ct)
        => Ok(await _clientas.ListarAsync(filtro ?? "todas", texto, ct));

    /// <summary>Totales del panel: cuántas clientas, nuevas de la semana, con cargo, cómo llegaron.</summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenClientasDto>> Resumen(CancellationToken ct)
        => Ok(await _clientas.ResumenAsync(ct));

    /// <summary>Ficha de una clienta.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ClientaResumenDto>> Obtener(string id, CancellationToken ct)
        => Ok(await _clientas.ObtenerAsync(id, ct));

    /// <summary>Alta en mostrador: nace sin PIN; si se registra con el mismo teléfono, reclama la ficha.</summary>
    [HttpPost]
    public async Task<ActionResult<ClientaDto>> Crear(CrearClientaInput input, CancellationToken ct)
        => Ok(await _clientas.CrearAsync(input, ct));
}
