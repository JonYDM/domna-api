using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// Equipo de la boutique: solo la dueña ve el personal (ella + cajeras) y da de alta cajeras nuevas.
/// </summary>
[ApiController]
[Route("api/personal")]
[Authorize(Roles = "duena")]
public class PersonalController : ControllerBase
{
    private readonly IPersonalService _personal;

    public PersonalController(IPersonalService personal) => _personal = personal;

    /// <summary>Lista a la dueña y las cajeras.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ClientaDto>>> Listar(CancellationToken ct)
        => Ok(await _personal.ListarAsync(ct));

    /// <summary>Da de alta una cajera: nombre, teléfono +52, PIN de 6 dígitos y su sucursal.</summary>
    [HttpPost]
    public async Task<ActionResult<ClientaDto>> CrearCajera(CrearCajeraInput input, CancellationToken ct)
        => Ok(await _personal.CrearCajeraAsync(input, ct));

    /// <summary>Edita nombre, teléfono y sucursal de una cajera.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ClientaDto>> EditarCajera(string id, EditarCajeraInput input, CancellationToken ct)
        => Ok(await _personal.EditarCajeraAsync(id, input, ct));

    /// <summary>Fija un PIN nuevo para una cajera que lo olvidó.</summary>
    [HttpPost("{id}/pin")]
    public async Task<ActionResult<ClientaDto>> ResetPin(string id, ResetPinInput input, CancellationToken ct)
        => Ok(await _personal.ResetPinAsync(id, input, ct));

    /// <summary>Bloquea el acceso de una cajera (conserva su historial).</summary>
    [HttpPost("{id}/desactivar")]
    public async Task<ActionResult<ClientaDto>> Desactivar(string id, CancellationToken ct)
        => Ok(await _personal.DesactivarAsync(id, ct));

    /// <summary>Restaura el acceso de una cajera desactivada.</summary>
    [HttpPost("{id}/activar")]
    public async Task<ActionResult<ClientaDto>> Activar(string id, CancellationToken ct)
        => Ok(await _personal.ActivarAsync(id, ct));
}
