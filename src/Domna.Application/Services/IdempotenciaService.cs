using System.Text.RegularExpressions;
using Domna.Application.Abstractions;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>Qué debe hacer el filtro/middleware tras consultar la idempotencia.</summary>
public enum ResultadoIdempotencia
{
    /// <summary>No hay llave o es nueva: ejecuta la acción y luego llama a <c>CompletarAsync</c>.</summary>
    Continuar,

    /// <summary>Ya hay una respuesta guardada para esta llave: devuélvela tal cual (Idempotent-Replay).</summary>
    Repetir,

    /// <summary>La primera petición con esta llave sigue en curso: responde 409.</summary>
    EnCurso,
}

/// <summary>Lo que el servicio devuelve al iniciar: qué hacer y, si aplica, la respuesta a repetir.</summary>
public record InicioIdempotencia(ResultadoIdempotencia Resultado, int CodigoEstado = 0, string? Cuerpo = null);

/// <summary>
/// Lógica reutilizable de idempotencia (sin acoplarse a HTTP, para poder probarla sola). El filtro
/// de la Api la invoca: antes de ejecutar la acción llama a <see cref="IniciarAsync"/>; si devuelve
/// Continuar, ejecuta la acción y luego llama a <see cref="CompletarAsync"/> con el resultado.
/// Semántica: misma llave + mismo usuario + misma ruta dentro de 24 h => no se re-ejecuta. Solo se
/// guardan respuestas 2xx. La limpieza de registros &gt; 24 h es perezosa (en cada inicio).
/// </summary>
public class IdempotenciaService : IIdempotenciaService
{
    /// <summary>8 a 64 caracteres [A-Za-z0-9_-]. Formato fijo que el front respeta.</summary>
    private static readonly Regex FormatoLlave = new("^[A-Za-z0-9_-]{8,64}$", RegexOptions.Compiled);

    /// <summary>Ventana de idempotencia: 24 horas.</summary>
    public static readonly TimeSpan Ventana = TimeSpan.FromHours(24);

    /// <summary>Límite razonable del cuerpo guardado (64 KB). Respuestas mayores no se cachean.</summary>
    public const int MaxCuerpoBytes = 64 * 1024;

    private readonly IIdempotenciaRepository _repo;
    private readonly IReloj _reloj;

    public IdempotenciaService(IIdempotenciaRepository repo, IReloj reloj)
    {
        _repo = repo;
        _reloj = reloj;
    }

    /// <summary>¿La llave respeta el formato del contrato? (null/vacía => no aplica idempotencia.)</summary>
    public static bool LlaveValida(string llave) => FormatoLlave.IsMatch(llave);

    public async Task<InicioIdempotencia> IniciarAsync(
        string usuarioId, string ruta, string? llave, CancellationToken ct = default)
    {
        // Sin cabecera: no aplica idempotencia, ejecuta normal.
        if (string.IsNullOrEmpty(llave))
            return new InicioIdempotencia(ResultadoIdempotencia.Continuar);

        // Formato inválido => 400 (lo traduce el filtro con DomnaException).
        if (!LlaveValida(llave))
            throw DomnaException.BadRequest("La cabecera Idempotency-Key debe tener de 8 a 64 caracteres (letras, números, '_' o '-').");

        var ahora = _reloj.Ahora;
        // Limpieza perezosa de registros vencidos (> 24 h), sin job aparte.
        await _repo.LimpiarVencidosAsync(ahora - Ventana, ct);

        var existente = await _repo.ObtenerAsync(usuarioId, ruta, llave, ct);
        if (existente is not null)
            return InterpretarExistente(existente, ahora);

        // No existe: reserva la llave como "en curso". Si otra instancia la reservó en la carrera,
        // vuelve a leer y responde según su estado.
        var reserva = Idempotencia.EnCurso(NuevoId(), usuarioId, ruta, llave, ahora);
        var reservado = await _repo.IntentarReservarAsync(reserva, ct);
        if (reservado)
            return new InicioIdempotencia(ResultadoIdempotencia.Continuar);

        var ganador = await _repo.ObtenerAsync(usuarioId, ruta, llave, ct);
        if (ganador is null) // caso límite: lo borraron entre medias; deja seguir.
            return new InicioIdempotencia(ResultadoIdempotencia.Continuar);
        return InterpretarExistente(ganador, ahora);
    }

    private static InicioIdempotencia InterpretarExistente(Idempotencia registro, DateTime ahora)
    {
        if (!registro.Terminada)
            return new InicioIdempotencia(ResultadoIdempotencia.EnCurso);
        return new InicioIdempotencia(ResultadoIdempotencia.Repetir, registro.CodigoEstado!.Value, registro.Cuerpo);
    }

    public async Task CompletarAsync(
        string usuarioId, string ruta, string llave, int codigoEstado, string? cuerpo, CancellationToken ct = default)
    {
        var registro = await _repo.ObtenerAsync(usuarioId, ruta, llave, ct);
        if (registro is null || registro.Terminada) return;

        var es2xx = codigoEstado is >= 200 and < 300;
        // Solo se guardan respuestas 2xx; los fallos se pueden reintentar con la misma llave.
        if (!es2xx)
        {
            await _repo.EliminarAsync(registro, ct);
            return;
        }

        // Respeta el límite del cuerpo: si se pasa, no se cachea (se borra la reserva para no colgarla).
        if (cuerpo is not null && System.Text.Encoding.UTF8.GetByteCount(cuerpo) > MaxCuerpoBytes)
        {
            await _repo.EliminarAsync(registro, ct);
            return;
        }

        registro.Completar(codigoEstado, cuerpo);
        await _repo.GuardarAsync(ct);
    }

    private static string NuevoId() => $"idem-{Guid.NewGuid():N}";
}
