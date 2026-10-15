using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Domna.Api;

/// <summary>
/// Middleware de caché HTTP "seguro por defecto" para todo lo que cuelga de <c>/api</c>:
///
///  1) POR DEFECTO: si al terminar la respuesta nadie fijó <c>Cache-Control</c>, se pone
///     <c>no-store</c> y <c>Vary: Authorization</c>. Así ninguna respuesta sensible termina en una
///     cache compartida por descuido. No pisa la Cache-Control que ya haya puesto el endpoint
///     (p. ej. <c>ApartadosController.VerComprobante</c> = <c>no-store</c>).
///
///  2) LISTA BLANCA (<see cref="PoliticaCacheHttp"/>): para los GET públicos del catálogo/config se
///     fija la Cache-Control calculada y, cuando corresponde, se calcula un <c>ETag</c> débil del
///     cuerpo y se responde <c>304 Not Modified</c> si el <c>If-None-Match</c> coincide.
///
/// Respeta 204/304 (sin cuerpo) y las respuestas de error del <see cref="ManejadorErrores"/>.
/// Debe ir DESPUÉS del manejador de errores y ANTES de los controllers.
/// </summary>
public class CacheHttpMiddleware
{
    private readonly RequestDelegate _next;

    public CacheHttpMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var req = ctx.Request;

        // Solo gobernamos /api. Lo demás (swagger, estáticos) sigue su curso sin tocar.
        if (!req.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var regla = PoliticaCacheHttp.ReglaPara(req, out var cacheable);

        // Caso con ETag/304: hay que ver el cuerpo, así que lo capturamos en memoria.
        if (regla is not null && cacheable)
        {
            await ManejarConETag(ctx, regla);
            return;
        }

        // Resto: fijamos la cabecera al vuelo justo antes de enviar, sin bufferizar.
        ctx.Response.OnStarting(() =>
        {
            AplicarCabeceras(ctx, regla);
            return Task.CompletedTask;
        });

        await _next(ctx);
    }

    /// <summary>
    /// Fija Cache-Control (regla de lista blanca o el no-store por defecto) y Vary: Authorization,
    /// sin duplicar ni pisar lo que ya exista. Se llama en OnStarting (cabeceras aún mutables).
    /// </summary>
    private static void AplicarCabeceras(HttpContext ctx, string? regla)
    {
        var resp = ctx.Response;

        // 304/204 no llevan cuerpo: no tiene sentido tocar su Cache-Control aquí (en 304 el ETag ya
        // se gestionó). Igual añadimos Vary para coherencia de caches intermedias.
        var sinCuerpo = resp.StatusCode is StatusCodes.Status304NotModified or StatusCodes.Status204NoContent;

        if (!sinCuerpo)
        {
            if (regla is not null)
            {
                // Lista blanca: imponemos la regla (es la fuente de verdad para estas rutas).
                resp.Headers[HeaderNames.CacheControl] = regla;
            }
            else if (!resp.Headers.ContainsKey(HeaderNames.CacheControl))
            {
                // Denegar por defecto: solo si el endpoint no puso la suya (no la pisamos).
                resp.Headers[HeaderNames.CacheControl] = PoliticaCacheHttp.PorDefecto;
            }
        }

        AgregarVaryAuthorization(resp);
    }

    /// <summary>Añade "Authorization" a Vary sin duplicarlo (la respuesta cambia según el token).</summary>
    private static void AgregarVaryAuthorization(HttpResponse resp)
    {
        var actual = resp.Headers[HeaderNames.Vary];
        var ya = actual.Any(v => v is not null && v.Split(',')
            .Any(p => string.Equals(p.Trim(), "Authorization", StringComparison.OrdinalIgnoreCase)));
        if (!ya)
            resp.Headers.Append(HeaderNames.Vary, "Authorization");
    }

    /// <summary>
    /// Ejecuta el pipeline capturando el cuerpo para calcular un ETag débil del contenido y, si el
    /// cliente mandó un If-None-Match que coincide, responde 304 sin cuerpo. Solo para respuestas
    /// 200 de la lista blanca; cualquier otro código (errores incluidos) se deja pasar tal cual.
    /// </summary>
    private async Task ManejarConETag(HttpContext ctx, string regla)
    {
        var resp = ctx.Response;
        var original = resp.Body;
        using var buffer = new MemoryStream();
        resp.Body = buffer;

        try
        {
            await _next(ctx);
        }
        catch
        {
            // Si algo explotó, restauramos el stream para que el ManejadorErrores escriba normal.
            resp.Body = original;
            throw;
        }

        buffer.Position = 0;

        // Solo aplicamos ETag/caché a un 200 con cuerpo. Errores/redirects/otros: copiar tal cual
        // y dejar que el no-store por defecto los cubra.
        var esOkConCuerpo = resp.StatusCode == StatusCodes.Status200OK && buffer.Length > 0;
        if (!esOkConCuerpo)
        {
            resp.Body = original;
            if (!resp.Headers.ContainsKey(HeaderNames.CacheControl))
                resp.Headers[HeaderNames.CacheControl] = PoliticaCacheHttp.PorDefecto;
            AgregarVaryAuthorization(resp);
            await buffer.CopyToAsync(original);
            return;
        }

        var cuerpo = buffer.ToArray();
        var etag = PoliticaCacheHttp.CalcularETag(cuerpo);

        resp.Headers[HeaderNames.CacheControl] = regla;
        resp.Headers[HeaderNames.ETag] = etag;
        AgregarVaryAuthorization(resp);

        var ifNoneMatch = ctx.Request.Headers[HeaderNames.IfNoneMatch].ToString();
        if (PoliticaCacheHttp.CoincideIfNoneMatch(ifNoneMatch, etag))
        {
            // 304: sin cuerpo. Conservamos ETag/Cache-Control/Vary y limpiamos Content-Length.
            resp.Body = original;
            resp.StatusCode = StatusCodes.Status304NotModified;
            resp.ContentLength = null;
            resp.Headers.Remove(HeaderNames.ContentType);
            return;
        }

        // 200 normal: devolvemos el cuerpo capturado al stream real.
        resp.Body = original;
        resp.ContentLength = cuerpo.Length;
        await original.WriteAsync(cuerpo);
    }
}
