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
}
