using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// Módulo de acceso de la clienta: registro (sign up), login y recuperación de PIN por correo.
/// Rutas alineadas con lo que consume el front (src/mock/server.ts).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ISesionService _sesiones;

    public AuthController(IAuthService auth, ISesionService sesiones)
    {
        _auth = auth;
        _sesiones = sesiones;
    }

    /// <summary>El front manda `X-Modo-App: instalada` cuando corre como app instalada (no en el navegador).</summary>
    private bool AppInstalada => string.Equals(Request.Headers["X-Modo-App"].ToString(), "instalada", StringComparison.OrdinalIgnoreCase);

    /// <summary>Registro propio: nombre, apellidos, teléfono +52, correo y PIN de 6 dígitos. Devuelve la sesión.</summary>
    [HttpPost("registrar")]
    public async Task<ActionResult<SesionDto>> Registrar(RegistroClientaInput input, CancellationToken ct)
        => Ok(await _auth.RegistrarAsync(input, Request.Headers.UserAgent.ToString(), ct, AppInstalada));

    /// <summary>Login con teléfono + PIN. Devuelve la sesión (JWT + clienta).</summary>
    [HttpPost("login")]
    public async Task<ActionResult<SesionDto>> Login(LoginInput input, CancellationToken ct)
        => Ok(await _auth.LoginAsync(input, Request.Headers.UserAgent.ToString(), ct, AppInstalada, HttpContext.Connection.RemoteIpAddress?.ToString()));

    /// <summary>
    /// ¿El teléfono ya tiene cuenta? Devuelve solo { existe }. No revela el nombre ni ningún dato
    /// (anti-enumeración): sirve para el saludo "Bienvenida de nuevo" sin exponer identidad.
    /// </summary>
    [HttpGet("existe")]
    public async Task<ActionResult<object>> Existe([FromQuery] string telefono, CancellationToken ct)
        => Ok(new { existe = await _auth.ExisteTelefonoAsync(telefono ?? "", ct) });

    /// <summary>Olvidé mi PIN: envía el enlace de recuperación por correo (en la demo devuelve el token).</summary>
    [HttpPost("recuperar")]
    public async Task<ActionResult<RecuperacionDto>> SolicitarRecuperacion(SolicitarRecuperacionInput input, CancellationToken ct)
        => Ok(await _auth.SolicitarRecuperacionAsync(input, ct));

    /// <summary>Confirma la recuperación con el token y fija un PIN nuevo. Devuelve la sesión.</summary>
    [HttpPost("recuperar/confirmar")]
    public async Task<ActionResult<SesionDto>> ConfirmarRecuperacion(ConfirmarRecuperacionInput input, CancellationToken ct)
        => Ok(await _auth.ConfirmarRecuperacionAsync(input, Request.Headers.UserAgent.ToString(), ct, AppInstalada));

    /// <summary>Cierra la sesión actual en el servidor (revoca su `sid`). Idempotente: siempre 204.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sid = User.FindFirst("sid")?.Value;
        if (!string.IsNullOrEmpty(sid))
            await _sesiones.RevocarAsync(sid, MotivosRevocacion.Cerrada, DateTime.UtcNow, ct);
        return NoContent();
    }

    /// <summary>Datos de la sesión actual (para que el front detecte pronto si fue reemplazada).</summary>
    [HttpGet("sesion")]
    [Authorize]
    public async Task<ActionResult<SesionActualDto>> Sesion(CancellationToken ct)
    {
        var sid = User.FindFirst("sid")?.Value;
        // Token legacy sin `sid`: no hay sesión persistida que mostrar.
        if (string.IsNullOrEmpty(sid))
            throw Domna.Domain.DomnaException.NotFound("Esta sesión no tiene estado en el servidor.");
        var actual = await _sesiones.ObtenerActualAsync(sid, ct)
            ?? throw Domna.Domain.DomnaException.NotFound("No encontramos la sesión.");
        return Ok(actual);
    }
}
