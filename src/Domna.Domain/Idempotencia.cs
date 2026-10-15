namespace Domna.Domain;

/// <summary>
/// Registro de una operación idempotente. El front manda una cabecera `Idempotency-Key` opcional en
/// ciertos POST; con la misma llave + mismo usuario + misma ruta dentro de 24 h NO se vuelve a
/// ejecutar: se devuelve la misma respuesta guardada. La llave se acota por usuario+ruta (no choca
/// con otra ruta ni con otro usuario). Un registro "en curso" (CodigoEstado nulo) significa que la
/// primera petición todavía no termina: una segunda con la misma llave responde 409.
/// Solo se guardan respuestas 2xx; los fallos se pueden reintentar con la misma llave.
/// </summary>
public class Idempotencia
{
    public string Id { get; private set; } = default!;
    public string UsuarioId { get; private set; } = default!;
    public string Ruta { get; private set; } = default!;
    public string Llave { get; private set; } = default!;

    /// <summary>Código HTTP guardado. Nulo mientras la operación sigue en curso.</summary>
    public int? CodigoEstado { get; private set; }

    /// <summary>Cuerpo de la respuesta guardada (solo para 2xx). Nulo mientras está en curso.</summary>
    public string? Cuerpo { get; private set; }

    public DateTime CreadoEl { get; private set; }

    private Idempotencia() { }

    /// <summary>Reserva la llave como "en curso" (todavía sin respuesta): bloquea reintentos simultáneos.</summary>
    public static Idempotencia EnCurso(string id, string usuarioId, string ruta, string llave, DateTime creadoEl) =>
        new()
        {
            Id = id,
            UsuarioId = usuarioId,
            Ruta = ruta,
            Llave = llave,
            CodigoEstado = null,
            Cuerpo = null,
            CreadoEl = creadoEl,
        };

    /// <summary>¿Ya terminó la operación (hay un código guardado)?</summary>
    public bool Terminada => CodigoEstado is not null;

    /// <summary>Guarda la respuesta final (código 2xx y cuerpo) sobre el registro en curso.</summary>
    public void Completar(int codigoEstado, string? cuerpo)
    {
        CodigoEstado = codigoEstado;
        Cuerpo = cuerpo;
    }
}
