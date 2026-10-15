using System.Text.Json;
using Domna.Domain;

namespace Domna.Api;

/// <summary>
/// Convierte las excepciones de dominio en respuestas HTTP con el código correcto y un cuerpo
/// JSON { "mensaje": "..." }. Cualquier otra excepción es un 500 genérico (sin filtrar detalles).
/// </summary>
public class ManejadorErrores
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ManejadorErrores> _log;

    public ManejadorErrores(RequestDelegate next, ILogger<ManejadorErrores> log)
    {
        _next = next;
        _log = log;
    }

    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (DomnaException ex)
        {
            await Escribir(ctx, ex.StatusCode, ex.Message, ex.Codigo, ex.Datos);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error no controlado");
            await Escribir(ctx, 500, "Ocurrió un error inesperado.");
        }
    }

    private static async Task Escribir(HttpContext ctx, int code, string mensaje, string? codigo = null, object? datos = null)
    {
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        // Si no hay código, el cuerpo queda EXACTAMENTE como siempre: { "mensaje": "..." }.
        // Si lo hay, se agregan "codigo" y (opcionalmente) "datos" en camelCase.
        object cuerpo = (codigo, datos) switch
        {
            (null, _) => new { mensaje },
            (_, null) => new { mensaje, codigo },
            _ => new { mensaje, codigo, datos },
        };
        var json = JsonSerializer.Serialize(cuerpo);
        await ctx.Response.WriteAsync(json);
    }
}
