using Domna.Application.Dtos;
using Domna.Application.Services;
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

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Registro propio: nombre, apellidos, teléfono +52, correo y PIN de 6 dígitos. Devuelve la sesión.</summary>
    [HttpPost("registrar")]
    public async Task<ActionResult<SesionDto>> Registrar(RegistroClientaInput input, CancellationToken ct)
        => Ok(await _auth.RegistrarAsync(input, ct));

    /// <summary>Login con teléfono + PIN. Devuelve la sesión (JWT + clienta).</summary>
    [HttpPost("login")]
    public async Task<ActionResult<SesionDto>> Login(LoginInput input, CancellationToken ct)
        => Ok(await _auth.LoginAsync(input, ct));

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
        => Ok(await _auth.ConfirmarRecuperacionAsync(input, ct));
}
