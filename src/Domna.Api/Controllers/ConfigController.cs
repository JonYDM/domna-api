using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>Configuración de negocio de la boutique. La lectura es pública (la tienda la necesita).</summary>
[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly IConfigService _config;

    public ConfigController(IConfigService config) => _config = config;

    [HttpGet]
    public async Task<ActionResult<ConfigBoutiqueDto>> Obtener(CancellationToken ct)
        => Ok(await _config.ObtenerAsync(ct));

    /// <summary>Kill switch del SaaS: pausar/reanudar la tienda. Solo la dueña.</summary>
    [HttpPost("suspendida")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ConfigBoutiqueDto>> FijarSuspendida([FromBody] bool suspendida, CancellationToken ct)
        => Ok(await _config.FijarSuspendidaAsync(suspendida, ct));

    /// <summary>Catálogo público u oculto sin cuenta. Solo la dueña.</summary>
    [HttpPost("catalogo-publico")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ConfigBoutiqueDto>> FijarCatalogoPublico([FromBody] bool publico, CancellationToken ct)
        => Ok(await _config.FijarCatalogoPublicoAsync(publico, ct));

    /// <summary>
    /// Cuenta para transferencias. Solo con sesión (no va en la config pública): la ve la clienta
    /// que elige pagar por transferencia.
    /// </summary>
    [HttpGet("datos-bancarios")]
    [Authorize]
    public async Task<ActionResult<DatosBancariosDto>> DatosBancarios(CancellationToken ct)
        => Ok(await _config.ObtenerDatosBancariosAsync(ct));

    /// <summary>Captura o cambia la cuenta para transferencias. Solo la dueña.</summary>
    [HttpPost("datos-bancarios")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<DatosBancariosDto>> FijarDatosBancarios(DatosBancariosInput input, CancellationToken ct)
        => Ok(await _config.FijarDatosBancariosAsync(input, ct));
}
