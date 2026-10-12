using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>Avisos in-app de la clienta (vencimientos, abonos, traslados, prendas listas).</summary>
[ApiController]
[Route("api/avisos")]
[Authorize]
public class AvisosController : ControllerBase
{
    private readonly IAvisoService _avisos;

    public AvisosController(IAvisoService avisos) => _avisos = avisos;

    [HttpGet("{clientaId}")]
    public async Task<ActionResult<List<AvisoDto>>> Listar(string clientaId, CancellationToken ct)
        => Ok(await _avisos.ListarAsync(clientaId, ct));

    [HttpPost("{clientaId}/leidos")]
    public async Task<IActionResult> MarcarLeidos(string clientaId, [FromBody] List<string> ids, CancellationToken ct)
    {
        await _avisos.MarcarLeidosAsync(clientaId, ids, ct);
        return NoContent();
    }
}
