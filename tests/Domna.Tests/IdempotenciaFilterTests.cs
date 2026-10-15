using System.Security.Claims;
using Domna.Api;
using Domna.Application.Services;
using Domna.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Prueba del filtro reutilizable de idempotencia tal como lo invoca MVC: lee la cabecera
/// Idempotency-Key, repite la respuesta 2xx guardada con `Idempotent-Replay: true`, responde 409 si
/// sigue en curso y 400 si el formato es inválido. Guarda solo respuestas 2xx.
/// </summary>
public class IdempotenciaFilterTests
{
    private readonly RelojFijo _reloj = new();
    private readonly IdempotenciaRepoFake _repo = new();
    private readonly IdempotenciaFilter _filtro;

    private const string Ruta = "/api/apartados";

    public IdempotenciaFilterTests()
    {
        var svc = new IdempotenciaService(_repo, _reloj);
        _filtro = new IdempotenciaFilter(svc);
    }

    private static DefaultHttpContext ContextoHttp(string usuarioId, string ruta, string? llave)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = ruta;
        if (llave is not null) ctx.Request.Headers[IdempotenciaFilter.CabeceraLlave] = llave;
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId),
        }, "test"));
        return ctx;
    }

    private static ActionExecutingContext Entrada(HttpContext http) =>
        new(
            new ActionContext(http, new RouteData(), new ControllerActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

    /// <summary>Ejecuta el filtro simulando que la acción devuelve <paramref name="resultado"/>.</summary>
    private async Task<ActionExecutedContext> Ejecutar(HttpContext http, IActionResult resultado)
    {
        var entrada = Entrada(http);
        ActionExecutedContext? salida = null;
        await _filtro.OnActionExecutionAsync(entrada, () =>
        {
            salida = new ActionExecutedContext(entrada, entrada.Filters, entrada.Controller)
            {
                Result = resultado,
            };
            return Task.FromResult(salida);
        });
        // Si el filtro cortó (repetición/corto-circuito), devuelve la entrada con su Result.
        return salida ?? new ActionExecutedContext(entrada, entrada.Filters, entrada.Controller) { Result = entrada.Result };
    }

    [Fact]
    public async Task Sin_cabecera_ejecuta_la_accion_normal_y_no_marca_repeticion()
    {
        var http = ContextoHttp("u1", Ruta, llave: null);
        var salida = await Ejecutar(http, new OkObjectResult(new { folio = "DOM-0001" }));

        Assert.IsType<OkObjectResult>(salida.Result);
        Assert.False(http.Response.Headers.ContainsKey(IdempotenciaFilter.CabeceraRepeticion));
    }

    [Fact]
    public async Task Formato_de_llave_invalido_lanza_400()
    {
        var http = ContextoHttp("u1", Ruta, "mala llave");
        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _filtro.OnActionExecutionAsync(Entrada(http), () => throw new InvalidOperationException("no debería ejecutar")));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task La_segunda_llamada_con_la_misma_llave_repite_la_respuesta_con_la_cabecera()
    {
        // Primera: ejecuta y guarda un 200 con cuerpo.
        var http1 = ContextoHttp("u1", Ruta, "llave-valida-1");
        await Ejecutar(http1, new OkObjectResult(new { folio = "DOM-0001" }));

        // Segunda: misma llave, misma ruta, mismo usuario -> repite sin ejecutar la acción.
        var http2 = ContextoHttp("u1", Ruta, "llave-valida-1");
        var entrada2 = Entrada(http2);
        var ejecutoAccion = false;
        await _filtro.OnActionExecutionAsync(entrada2, () =>
        {
            ejecutoAccion = true;
            return Task.FromResult(new ActionExecutedContext(entrada2, entrada2.Filters, entrada2.Controller));
        });

        Assert.False(ejecutoAccion); // no se re-ejecutó
        Assert.Equal("true", http2.Response.Headers[IdempotenciaFilter.CabeceraRepeticion].ToString());
        var content = Assert.IsType<ContentResult>(entrada2.Result);
        Assert.Equal(200, content.StatusCode);
        Assert.Contains("DOM-0001", content.Content);
    }

    [Fact]
    public async Task La_misma_llave_mientras_sigue_en_curso_responde_409()
    {
        // Simula que la primera petición ya reservó la llave y aún no termina.
        await _repo.IntentarReservarAsync(
            Idempotencia.EnCurso("x", "u1", Ruta, "llave-valida-2", _reloj.Ahora));

        var http = ContextoHttp("u1", Ruta, "llave-valida-2");
        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _filtro.OnActionExecutionAsync(Entrada(http), () =>
                throw new InvalidOperationException("no debería ejecutar")));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Un_fallo_no_se_guarda_y_la_llave_se_puede_reintentar()
    {
        var http = ContextoHttp("u1", Ruta, "llave-valida-3");
        // La acción devuelve 400 (BadRequest): no debe cachearse.
        await Ejecutar(http, new BadRequestObjectResult(new { mensaje = "error" }));

        Assert.Null(await _repo.ObtenerAsync("u1", Ruta, "llave-valida-3"));
    }

    [Fact]
    public async Task Guarda_el_204_sin_cuerpo_y_lo_repite()
    {
        var http1 = ContextoHttp("u1", "/api/stock/mover", "llave-valida-4");
        await Ejecutar(http1, new NoContentResult()); // 204

        var http2 = ContextoHttp("u1", "/api/stock/mover", "llave-valida-4");
        var entrada2 = Entrada(http2);
        await _filtro.OnActionExecutionAsync(entrada2, () =>
            Task.FromResult(new ActionExecutedContext(entrada2, entrada2.Filters, entrada2.Controller)));

        Assert.Equal("true", http2.Response.Headers[IdempotenciaFilter.CabeceraRepeticion].ToString());
        var repetido = Assert.IsType<StatusCodeResult>(entrada2.Result);
        Assert.Equal(204, repetido.StatusCode);
    }
}
