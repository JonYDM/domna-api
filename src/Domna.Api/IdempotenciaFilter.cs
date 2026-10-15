using System.Security.Claims;
using System.Text.Json;
using Domna.Application.Services;
using Domna.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Domna.Api;

/// <summary>
/// Marca un endpoint POST como idempotente. El front manda una cabecera opcional `Idempotency-Key`;
/// con la misma llave + mismo usuario + misma ruta dentro de 24 h NO se re-ejecuta: se devuelve la
/// misma respuesta guardada (con `Idempotent-Replay: true`). Reutilizable: toda la lógica vive en el
/// filtro, no se copia en cada endpoint. Se aplica como <c>[ServiceFilter(typeof(IdempotenciaFilter))]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotenteAttribute : ServiceFilterAttribute
{
    public IdempotenteAttribute() : base(typeof(IdempotenciaFilter)) { }
}

/// <summary>
/// Filtro reutilizable que aplica la idempotencia alrededor de la acción, apoyándose en
/// <see cref="IIdempotenciaService"/>. Se registra como scoped en la DI.
/// </summary>
public sealed class IdempotenciaFilter : IAsyncActionFilter
{
    public const string CabeceraLlave = "Idempotency-Key";
    public const string CabeceraRepeticion = "Idempotent-Replay";

    private readonly IIdempotenciaService _idempotencia;

    public IdempotenciaFilter(IIdempotenciaService idempotencia) => _idempotencia = idempotencia;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var llave = http.Request.Headers[CabeceraLlave].ToString();
        var llaveNormalizada = string.IsNullOrWhiteSpace(llave) ? null : llave.Trim();

        var usuarioId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? "anonimo";
        // La ruta acota la llave: misma llave en otra ruta no colisiona. Normalizada en minúsculas.
        var ruta = (http.Request.Path.Value ?? "").ToLowerInvariant();
        var ct = http.RequestAborted;

        // IniciarAsync lanza DomnaException(400) si el formato de la llave es inválido (lo traduce el middleware).
        var inicio = await _idempotencia.IniciarAsync(usuarioId, ruta, llaveNormalizada, ct);

        switch (inicio.Resultado)
        {
            case ResultadoIdempotencia.Repetir:
                http.Response.Headers[CabeceraRepeticion] = "true";
                context.Result = RespuestaGuardada(inicio.CodigoEstado, inicio.Cuerpo);
                return;

            case ResultadoIdempotencia.EnCurso:
                throw new DomnaException(409,
                    "Esta operación con la misma Idempotency-Key sigue en curso. Espera a que termine antes de reintentar.");

            case ResultadoIdempotencia.Continuar:
            default:
                break;
        }

        var ejecutado = await next();

        // Sin llave válida no hay nada que guardar.
        if (llaveNormalizada is null) return;

        // Si la acción lanzó una excepción, se maneja como fallo: no se cachea (reintentable).
        if (ejecutado.Exception is not null && !ejecutado.ExceptionHandled)
        {
            await _idempotencia.CompletarAsync(usuarioId, ruta, llaveNormalizada!, 500, null, ct);
            return;
        }

        var (codigo, cuerpo) = ExtraerRespuesta(ejecutado.Result, http.Response.StatusCode);
        await _idempotencia.CompletarAsync(usuarioId, ruta, llaveNormalizada!, codigo, cuerpo, ct);
    }

    /// <summary>Reconstruye la respuesta guardada: mismo código y cuerpo JSON (si hubo).</summary>
    private static IActionResult RespuestaGuardada(int codigo, string? cuerpo)
    {
        if (cuerpo is null)
            return new StatusCodeResult(codigo);
        return new ContentResult
        {
            StatusCode = codigo,
            ContentType = "application/json",
            Content = cuerpo,
        };
    }

    /// <summary>Deduce el código HTTP y serializa el cuerpo del resultado de la acción.</summary>
    private static (int Codigo, string? Cuerpo) ExtraerRespuesta(IActionResult? resultado, int statusActual)
    {
        switch (resultado)
        {
            case ObjectResult obj:
                var codigoObj = obj.StatusCode ?? 200;
                var cuerpo = obj.Value is null ? null : JsonSerializer.Serialize(obj.Value, OpcionesJson);
                return (codigoObj, cuerpo);

            case StatusCodeResult sc: // p. ej. NoContent (204)
                return (sc.StatusCode, null);

            case ContentResult cr:
                return (cr.StatusCode ?? 200, cr.Content);

            case null:
                return (statusActual, null);

            default:
                return (statusActual, null);
        }
    }

    // Mismas opciones que usa ASP.NET por defecto para los cuerpos JSON (camelCase).
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);
}
