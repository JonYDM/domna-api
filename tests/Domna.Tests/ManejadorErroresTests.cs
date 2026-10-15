using System.Text;
using System.Text.Json;
using Domna.Api;
using Domna.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Pruebas del middleware de errores: que serialice `codigo`/`datos` en camelCase cuando la
/// DomnaException los trae, y que el cuerpo quede EXACTAMENTE como antes (solo { mensaje }) cuando no.
/// </summary>
public class ManejadorErroresTests
{
    private static async Task<(int Status, string Cuerpo)> Ejecutar(Exception aLanzar)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var manejador = new ManejadorErrores(_ => throw aLanzar, NullLogger<ManejadorErrores>.Instance);
        await manejador.Invoke(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var cuerpo = await new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (ctx.Response.StatusCode, cuerpo);
    }

    [Fact]
    public async Task Sin_codigo_el_cuerpo_queda_igual_que_siempre()
    {
        var (status, cuerpo) = await Ejecutar(DomnaException.Unauthorized("Teléfono o PIN incorrecto."));

        Assert.Equal(401, status);
        var doc = JsonDocument.Parse(cuerpo).RootElement;
        // Solo "mensaje": NO aparecen "codigo" ni "datos".
        Assert.Equal("Teléfono o PIN incorrecto.", doc.GetProperty("mensaje").GetString());
        Assert.False(doc.TryGetProperty("codigo", out _));
        Assert.False(doc.TryGetProperty("datos", out _));
    }

    [Fact]
    public async Task Con_codigo_y_datos_los_serializa_en_camelCase()
    {
        var ex = DomnaException.Conflict(
            "Ya tienes una sesion abierta en otro dispositivo.",
            codigo: "sesion_activa",
            datos: new { dispositivo = "Android · Chrome", creada = "2026-01-01T12:00:00Z" });

        var (status, cuerpo) = await Ejecutar(ex);

        Assert.Equal(409, status);
        var doc = JsonDocument.Parse(cuerpo).RootElement;
        Assert.Equal("Ya tienes una sesion abierta en otro dispositivo.", doc.GetProperty("mensaje").GetString());
        Assert.Equal("sesion_activa", doc.GetProperty("codigo").GetString());
        var datos = doc.GetProperty("datos");
        Assert.Equal("Android · Chrome", datos.GetProperty("dispositivo").GetString());
        Assert.Equal("2026-01-01T12:00:00Z", datos.GetProperty("creada").GetString());
    }

    [Fact]
    public async Task Con_codigo_sin_datos_incluye_codigo_pero_no_datos()
    {
        var (status, cuerpo) = await Ejecutar(
            DomnaException.Unauthorized("Tu sesión se cerró.", codigo: "sesion_cerrada"));

        Assert.Equal(401, status);
        var doc = JsonDocument.Parse(cuerpo).RootElement;
        Assert.Equal("sesion_cerrada", doc.GetProperty("codigo").GetString());
        Assert.False(doc.TryGetProperty("datos", out _));
    }
}
