using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Autenticación de la clienta. Replica las reglas de src/mock/clientas.ts:
/// - Teléfono y correo únicos; el teléfono es la llave de acceso.
/// - Si el teléfono ya existía por un alta en mostrador SIN PIN, el registro "reclama" esa ficha.
/// - Login con mensaje genérico (no revela si falló el teléfono o el PIN).
/// - Recuperación por correo: token de un solo uso, 30 min; no revela si el correo existe.
/// El PIN se guarda siempre como hash BCrypt.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IClientaRepository _clientas;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtGenerator _jwt;
    private readonly IServicioCorreo _correo;
    private readonly IReloj _reloj;

    public AuthService(
        IClientaRepository clientas,
        IPasswordHasher hasher,
        IJwtGenerator jwt,
        IServicioCorreo correo,
        IReloj reloj)
    {
        _clientas = clientas;
        _hasher = hasher;
        _jwt = jwt;
        _correo = correo;
        _reloj = reloj;
    }

    public async Task<SesionDto> RegistrarAsync(RegistroClientaInput input, CancellationToken ct = default)
    {
        // Normaliza/valida con las reglas de dominio antes de tocar la base.
        var telefono = Normalizacion.NormalizarTelefono(input.Telefono);
        var email = Normalizacion.NormalizarEmail(input.Email);
        var pin = Normalizacion.NormalizarPin(input.Pin);
        var pinHash = _hasher.Hash(pin);

        var existentePorTel = await _clientas.ObtenerPorTelefonoAsync(telefono, ct);

        // Reclamar una ficha de mostrador que aún no tiene acceso (sin PIN).
        if (existentePorTel is not null && !existentePorTel.TieneAcceso)
        {
            var dueñoEmail = await _clientas.ObtenerPorEmailAsync(email, ct);
            if (dueñoEmail is not null && dueñoEmail.Id != existentePorTel.Id)
                throw DomnaException.Conflict("Ese correo ya está en uso.");

            existentePorTel.ReclamarComoRegistro(input.Nombre, input.Apellidos, email, pinHash, _reloj.Ahora);
            await _clientas.GuardarAsync(ct);
            return Sesion(existentePorTel);
        }

        if (existentePorTel is not null)
            throw DomnaException.Conflict("Ya hay una cuenta con ese teléfono. Inicia sesión.");
        if (await _clientas.ObtenerPorEmailAsync(email, ct) is not null)
            throw DomnaException.Conflict("Ese correo ya está en uso.");

        var clienta = Clienta.Crear(
            NuevoId(),
            input.Nombre,
            input.Apellidos,
            telefono,
            email,
            OrigenClienta.Registro,
            _reloj.Ahora,
            pinHash);

        await _clientas.AgregarAsync(clienta, ct);
        await _clientas.GuardarAsync(ct);
        return Sesion(clienta);
    }

    public async Task<SesionDto> LoginAsync(LoginInput input, CancellationToken ct = default)
    {
        var telefono = Normalizacion.NormalizarTelefono(input.Telefono);
        var pin = Normalizacion.NormalizarPin(input.Pin);

        var clienta = await _clientas.ObtenerPorTelefonoAsync(telefono, ct);
        // Mensaje genérico: no filtrar qué dato falló.
        if (clienta is null || !clienta.TieneAcceso || !_hasher.Verificar(pin, clienta.PinHash!))
            throw DomnaException.Unauthorized("Teléfono o PIN incorrecto.");

        clienta.RegistrarAcceso(_reloj.Ahora);
        await _clientas.GuardarAsync(ct);
        return Sesion(clienta);
    }

    public async Task<bool> ExisteTelefonoAsync(string telefono, CancellationToken ct = default)
    {
        // Tolerante: si el teléfono no es válido aún, responde "no existe" en vez de lanzar 400.
        string normalizado;
        try
        {
            normalizado = Normalizacion.NormalizarTelefono(telefono);
        }
        catch (DomnaException)
        {
            return false;
        }
        var clienta = await _clientas.ObtenerPorTelefonoAsync(normalizado, ct);
        // Solo cuenta como "existe" si tiene acceso (PIN establecido): puede iniciar sesión.
        return clienta is not null && clienta.TieneAcceso;
    }

    public async Task<RecuperacionDto> SolicitarRecuperacionAsync(SolicitarRecuperacionInput input, CancellationToken ct = default)
    {
        var email = Normalizacion.NormalizarEmail(input.Email);
        var clienta = await _clientas.ObtenerPorEmailAsync(email, ct);
        // No revelar si el correo existe: si no está, responde en silencio (token null).
        if (clienta is null)
            return new RecuperacionDto(null);

        var token = $"rec-{Guid.NewGuid():N}";
        var recuperacion = RecuperacionPin.Crear(token, clienta.Id, _reloj.Ahora);
        await _clientas.AgregarRecuperacionAsync(recuperacion, ct);
        await _clientas.GuardarAsync(ct);
        await _correo.EnviarRecuperacionAsync(email, token, ct);
        return new RecuperacionDto(token);
    }

    public async Task<SesionDto> ConfirmarRecuperacionAsync(ConfirmarRecuperacionInput input, CancellationToken ct = default)
    {
        var pin = Normalizacion.NormalizarPin(input.Pin);
        var recuperacion = await _clientas.ObtenerRecuperacionAsync((input.Token ?? string.Empty).Trim(), ct);
        if (recuperacion is null || !recuperacion.EsValido(_reloj.Ahora))
            throw DomnaException.BadRequest("El enlace de recuperación no es válido o ya venció.");

        var clienta = await _clientas.ObtenerPorIdAsync(recuperacion.ClientaId, ct)
            ?? throw DomnaException.NotFound("Clienta no encontrada.");

        clienta.FijarPinHash(_hasher.Hash(pin));
        clienta.RegistrarAcceso(_reloj.Ahora);
        recuperacion.Consumir(); // un solo uso
        await _clientas.GuardarAsync(ct);
        return Sesion(clienta);
    }

    private SesionDto Sesion(Clienta clienta)
    {
        var (token, expira) = _jwt.Generar(clienta, clienta.Rol);
        return new SesionDto(token, expira, clienta.ToDto());
    }

    private static string NuevoId() => $"c-{Guid.NewGuid():N}"[..12];
}
