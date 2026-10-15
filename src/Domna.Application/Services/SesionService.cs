using Domna.Application.Abstractions;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>Resultado de validar un `sid`: por qué una sesión es o no válida en una petición.</summary>
public enum EstadoSesion
{
    /// <summary>La sesión existe y sigue activa (no revocada, no vencida).</summary>
    Activa,
    /// <summary>Revocada porque se entró desde otro dispositivo y se eligió reemplazarla.</summary>
    Reemplazada,
    /// <summary>Revocada por un logout explícito.</summary>
    Cerrada,
    /// <summary>Revocada porque cambió el PIN (recuperación o reset de la dueña).</summary>
    PinCambiado,
    /// <summary>Revocada porque se desactivó la cuenta (cajera).</summary>
    Desactivada,
    /// <summary>Existía pero su fecha ya venció.</summary>
    Expirada,
    /// <summary>No existe ninguna sesión con ese id (token viejo, base reiniciada, etc.).</summary>
    Invalida,
}

/// <summary>Datos de una sesión activa que el front consulta (GET api/auth/sesion).</summary>
public record SesionActualDto(string Dispositivo, DateTime Creada, DateTime UltimaActividad, DateTime Expira);

/// <summary>Casos de uso de las sesiones (una sola activa por cuenta).</summary>
public interface ISesionService
{
    /// <summary>Crea una sesión nueva para un usuario y la persiste. El `sesionId` ya viene generado.</summary>
    Task CrearAsync(string sesionId, string usuarioId, string dispositivo, DateTime expira, CancellationToken ct = default);

    /// <summary>Sesiones ACTIVAS (no revocadas, no vencidas) de un usuario, en el momento dado.</summary>
    Task<List<Sesion>> ListarActivasAsync(string usuarioId, DateTime ahora, CancellationToken ct = default);

    /// <summary>Revoca todas las sesiones activas de un usuario con un motivo. Devuelve cuántas revocó.</summary>
    Task<int> RevocarTodasAsync(string usuarioId, string motivo, DateTime ahora, CancellationToken ct = default);

    /// <summary>Revoca una sesión puntual (por id). Idempotente: si no existe o ya estaba revocada, no falla.</summary>
    Task RevocarAsync(string sesionId, string motivo, DateTime ahora, CancellationToken ct = default);

    /// <summary>Valida el estado de un `sid` en el momento dado (para la validación por petición, sin HTTP).</summary>
    Task<EstadoSesion> ValidarAsync(string sesionId, DateTime ahora, CancellationToken ct = default);

    /// <summary>
    /// Marca actividad reciente de la sesión (como máximo una vez por minuto). Devuelve true si se
    /// escribió en la base. Si no existe o no toca actualizar, devuelve false.
    /// </summary>
    Task<bool> TocarAsync(string sesionId, DateTime ahora, CancellationToken ct = default);

    /// <summary>La sesión actual (para GET api/auth/sesion). Null si no existe.</summary>
    Task<SesionActualDto?> ObtenerActualAsync(string sesionId, CancellationToken ct = default);
}

/// <summary>
/// Lógica de "una sola sesión activa por cuenta": crear sesiones, listarlas, revocarlas (una o todas)
/// y validar un `sid`. La validación devuelve un <see cref="EstadoSesion"/> para que la capa HTTP la
/// traduzca a 401 con el código correcto, pero la regla vive aquí y es testeable sin servidor.
/// </summary>
public class SesionService : ISesionService
{
    private readonly ISesionRepository _sesiones;
    private readonly IReloj _reloj;
    private readonly ICacheSesion _cache;

    public SesionService(ISesionRepository sesiones, IReloj reloj, ICacheSesion cache)
    {
        _sesiones = sesiones;
        _reloj = reloj;
        _cache = cache;
    }

    public async Task CrearAsync(string sesionId, string usuarioId, string dispositivo, DateTime expira, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var sesion = Sesion.Crear(sesionId, usuarioId, dispositivo, ahora, expira);
        await _sesiones.AgregarAsync(sesion, ct);
        await _sesiones.GuardarAsync(ct);
    }

    public async Task<List<Sesion>> ListarActivasAsync(string usuarioId, DateTime ahora, CancellationToken ct = default)
    {
        var todas = await _sesiones.ListarNoRevocadasDeUsuarioAsync(usuarioId, ct);
        return todas.Where(s => s.EstaActiva(ahora)).ToList();
    }

    public async Task<int> RevocarTodasAsync(string usuarioId, string motivo, DateTime ahora, CancellationToken ct = default)
    {
        var activas = await ListarActivasAsync(usuarioId, ahora, ct);
        if (activas.Count == 0) return 0;
        foreach (var s in activas)
            s.Revocar(motivo, ahora);
        await _sesiones.GuardarAsync(ct);
        foreach (var s in activas)
            _cache.Invalidar(s.Id);
        return activas.Count;
    }

    public async Task RevocarAsync(string sesionId, string motivo, DateTime ahora, CancellationToken ct = default)
    {
        var sesion = await _sesiones.ObtenerPorIdAsync(sesionId, ct);
        if (sesion is null) return; // idempotente: nada que revocar
        if (sesion.Revocada) return; // ya estaba revocada: no cambia el motivo original
        sesion.Revocar(motivo, ahora);
        await _sesiones.GuardarAsync(ct);
        _cache.Invalidar(sesionId);
    }

    public async Task<EstadoSesion> ValidarAsync(string sesionId, DateTime ahora, CancellationToken ct = default)
    {
        var sesion = await _sesiones.ObtenerPorIdAsync(sesionId, ct);
        if (sesion is null) return EstadoSesion.Invalida;
        if (sesion.Revocada) return MotivoAEstado(sesion.MotivoRevocacion);
        if (sesion.Expira <= ahora) return EstadoSesion.Expirada;
        return EstadoSesion.Activa;
    }

    public async Task<bool> TocarAsync(string sesionId, DateTime ahora, CancellationToken ct = default)
    {
        var sesion = await _sesiones.ObtenerPorIdAsync(sesionId, ct);
        if (sesion is null || !sesion.EstaActiva(ahora)) return false;
        if (!sesion.Tocar(ahora)) return false; // no pasó un minuto: no escribimos
        await _sesiones.GuardarAsync(ct);
        return true;
    }

    public async Task<SesionActualDto?> ObtenerActualAsync(string sesionId, CancellationToken ct = default)
    {
        var sesion = await _sesiones.ObtenerPorIdAsync(sesionId, ct);
        if (sesion is null) return null;
        return new SesionActualDto(sesion.Dispositivo, sesion.Creada, sesion.UltimaActividad, sesion.Expira);
    }

    /// <summary>Motivos de revocación conocidos → estado para el front. Motivo desconocido ⇒ Invalida.</summary>
    private static EstadoSesion MotivoAEstado(string? motivo) => motivo switch
    {
        MotivosRevocacion.Reemplazada => EstadoSesion.Reemplazada,
        MotivosRevocacion.Cerrada => EstadoSesion.Cerrada,
        MotivosRevocacion.PinCambiado => EstadoSesion.PinCambiado,
        MotivosRevocacion.Desactivada => EstadoSesion.Desactivada,
        _ => EstadoSesion.Invalida,
    };
}

/// <summary>Motivos de revocación de sesión (texto que se guarda en la base y usa el front).</summary>
public static class MotivosRevocacion
{
    public const string Reemplazada = "reemplazada";
    public const string Cerrada = "cerrada";
    public const string PinCambiado = "pin_cambiado";
    public const string Desactivada = "desactivada";
}
