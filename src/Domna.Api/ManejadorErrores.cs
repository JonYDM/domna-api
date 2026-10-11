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
            await Escribir(ctx, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error no controlado");
            await Escribir(ctx, 500, "Ocurrió un error inesperado.");
        }
    }

    private static async Task Escribir(HttpContext ctx, int code, string mensaje)
    {
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(new { mensaje });
        await ctx.Response.WriteAsync(json);
    }
}
