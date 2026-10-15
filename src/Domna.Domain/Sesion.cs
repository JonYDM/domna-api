namespace Domna.Domain;

/// <summary>
/// Sesión activa de una cuenta en un dispositivo. Soporta la regla de "una sola sesión activa por
/// cuenta": cada login/registro crea una Sesión y, al entrar desde otro dispositivo, el servidor
/// decide si la reemplaza (revoca las anteriores) según lo que pida el usuario.
///
/// El id de la sesión viaja en el claim `sid` del JWT; en cada petición autenticada se valida que
/// la sesión siga activa (no revocada y no vencida). Las sesiones vencidas se ignoran (no hace falta
/// un job de limpieza; se puede hacer limpieza perezosa).
/// </summary>
public class Sesion
{
    public string Id { get; private set; } = default!;
    public string UsuarioId { get; private set; } = default!;
    public DateTime Creada { get; private set; }

    /// <summary>Última petición autenticada vista con esta sesión. Se actualiza como máximo una vez por minuto.</summary>
    public DateTime UltimaActividad { get; private set; }

    /// <summary>Cuándo deja de valer (coincide con la expiración del JWT que la creó).</summary>
    public DateTime Expira { get; private set; }

    /// <summary>Texto corto del dispositivo/navegador (p. ej. "Android · Chrome"). Para que el front lo muestre.</summary>
    public string Dispositivo { get; private set; } = default!;

    public bool Revocada { get; private set; }

    /// <summary>Por qué se revocó: "reemplazada", "cerrada", "pin_cambiado", "desactivada". Null si sigue activa.</summary>
    public string? MotivoRevocacion { get; private set; }

    public DateTime? RevocadaEl { get; private set; }

    private Sesion() { }

    /// <summary>Crea una sesión nueva para una cuenta, con la expiración del token que la respalda.</summary>
    public static Sesion Crear(string id, string usuarioId, string dispositivo, DateTime ahora, DateTime expira) =>
        new()
        {
            Id = id,
            UsuarioId = usuarioId,
            Dispositivo = string.IsNullOrWhiteSpace(dispositivo) ? "Otro dispositivo" : dispositivo,
            Creada = ahora,
            UltimaActividad = ahora,
            Expira = expira,
            Revocada = false,
            MotivoRevocacion = null,
            RevocadaEl = null,
        };

    /// <summary>¿La sesión sigue activa? (no revocada y no vencida en el momento dado).</summary>
    public bool EstaActiva(DateTime ahora) => !Revocada && Expira > ahora;

    /// <summary>Revoca la sesión con un motivo. Idempotente: si ya estaba revocada, no cambia el motivo original.</summary>
    public void Revocar(string motivo, DateTime ahora)
    {
        if (Revocada) return;
        Revocada = true;
        MotivoRevocacion = motivo;
        RevocadaEl = ahora;
    }

    /// <summary>
    /// Marca actividad reciente, pero solo si pasó al menos un minuto desde la última (para no
    /// escribir en la base en cada petición). Devuelve true si realmente se actualizó.
    /// </summary>
    public bool Tocar(DateTime ahora)
    {
        if (ahora - UltimaActividad < TimeSpan.FromMinutes(1)) return false;
        UltimaActividad = ahora;
        return true;
    }
}
