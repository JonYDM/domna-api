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
    private readonly ISesionService _sesiones;

    public AuthService(
        IClientaRepository clientas,
        IPasswordHasher hasher,
        IJwtGenerator jwt,
        IServicioCorreo correo,
        IReloj reloj,
        ISesionService sesiones)
    {
        _clientas = clientas;
        _hasher = hasher;
        _jwt = jwt;
        _correo = correo;
        _reloj = reloj;
        _sesiones = sesiones;
    }

    public async Task<SesionDto> RegistrarAsync(RegistroClientaInput input, string? userAgent = null, CancellationToken ct = default, bool appInstalada = false)
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
            return await SesionAsync(existentePorTel, userAgent, appInstalada, ct);
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
        return await SesionAsync(clienta, userAgent, appInstalada, ct);
    }

    public async Task<SesionDto> LoginAsync(LoginInput input, string? userAgent = null, CancellationToken ct = default, bool appInstalada = false)
    {
        var telefono = Normalizacion.NormalizarTelefono(input.Telefono);
        var pin = Normalizacion.NormalizarPin(input.Pin);

        var clienta = await _clientas.ObtenerPorTelefonoAsync(telefono, ct);
        // Mensaje genérico: no filtrar qué dato falló (ni si la cuenta está desactivada).
        // IMPORTANTE: validamos las credenciales ANTES de mirar si hay otra sesión abierta, para no
        // revelar (con un 409) que ese teléfono tiene cuenta cuando el PIN es incorrecto.
        if (clienta is null || !clienta.TieneAcceso || !clienta.Activo || !_hasher.Verificar(pin, clienta.PinHash!))
            throw DomnaException.Unauthorized("Teléfono o PIN incorrecto.");

        // Credenciales válidas. ¿Hay otra sesión activa en otro dispositivo?
        var activas = await _sesiones.ListarActivasAsync(clienta.Id, _reloj.Ahora, ct);
        if (activas.Count > 0 && !input.ReemplazarSesion)
        {
            // No emitimos token ni tocamos la otra sesión: devolvemos 409 con los datos para que el
            // front pregunte en cuál dispositivo seguir. Mostramos la sesión más reciente.
            var otra = activas.OrderByDescending(s => s.UltimaActividad).First();
            throw DomnaException.Conflict(
                "Ya tienes una sesion abierta en otro dispositivo.",
                codigo: "sesion_activa",
                datos: new
                {
                    dispositivo = otra.Dispositivo,
                    creada = otra.Creada,
                    ultimaActividad = otra.UltimaActividad,
                });
        }

        // Con reemplazarSesion=true (o sin sesiones previas), revocamos las activas y creamos la nueva.
        if (activas.Count > 0)
            await _sesiones.RevocarTodasAsync(clienta.Id, MotivosRevocacion.Reemplazada, _reloj.Ahora, ct);

        clienta.RegistrarAcceso(_reloj.Ahora);
        await _clientas.GuardarAsync(ct);
        return await SesionAsync(clienta, userAgent, appInstalada, ct);
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

    public async Task<SesionDto> ConfirmarRecuperacionAsync(ConfirmarRecuperacionInput input, string? userAgent = null, CancellationToken ct = default, bool appInstalada = false)
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

        // Cambió el PIN: todas las sesiones previas dejan de valer (motivo "pin_cambiado").
        await _sesiones.RevocarTodasAsync(clienta.Id, MotivosRevocacion.PinCambiado, _reloj.Ahora, ct);

        return await SesionAsync(clienta, userAgent, appInstalada, ct);
    }

    /// <summary>
    /// Emite la sesión: genera el id, el token (que lleva el `sid`) y crea la Sesión persistida con la
    /// Expira del token. Orden: id → token → Sesión.
    /// </summary>
    private async Task<SesionDto> SesionAsync(Clienta clienta, string? userAgent, bool appInstalada, CancellationToken ct)
    {
        var sesionId = NuevoSesionId();
        var (token, expira) = _jwt.Generar(clienta, clienta.Rol, sesionId);
        var dispositivo = DescripcionDispositivo.De(userAgent, appInstalada);
        await _sesiones.CrearAsync(sesionId, clienta.Id, dispositivo, expira, ct);
        return new SesionDto(token, expira, clienta.ToDto());
    }

    private static string NuevoId() => $"c-{Guid.NewGuid():N}"[..12];

    private static string NuevoSesionId() => $"s-{Guid.NewGuid():N}";
}
