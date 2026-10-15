using System.Text;
using Domna.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Pruebas del middleware de caché con <see cref="DefaultHttpContext"/> (sin servidor HTTP real).
/// Cubren la política "no-store por defecto" + lista blanca, la excepción incluirInactivos, el
/// ETag/304 y que las rutas sensibles nunca se cachean.
/// </summary>
public class CacheHttpMiddlewareTests
{
    /// <summary>
    /// Ejecuta el middleware sobre un contexto fabricado. <paramref name="cuerpo"/> es lo que
    /// "escribe el controller"; <paramref name="statusCode"/> y <paramref name="cacheControlEndpoint"/>
    /// simulan lo que el endpoint ya dejó puesto. Devuelve el contexto tras correr el middleware.
    /// </summary>
    private static async Task<HttpContext> Ejecutar(
        string metodo,
        string path,
        string? queryIncluirInactivos = null,
        string? ifNoneMatch = null,
        int statusCode = StatusCodes.Status200OK,
        string cuerpo = "{\"ok\":true}",
        string? cacheControlEndpoint = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = metodo;
        ctx.Request.Path = path;
        if (queryIncluirInactivos is not null)
            ctx.Request.QueryString = new QueryString($"?incluirInactivos={queryIncluirInactivos}");
        if (ifNoneMatch is not null)
            ctx.Request.Headers[HeaderNames.IfNoneMatch] = ifNoneMatch;

        // DefaultHttpContext usa un IHttpResponseFeature cuyos callbacks OnStarting NO se ejecutan.
        // Montamos uno que SÍ los guarda y los dispara (como un servidor real), con un MemoryStream
        // como cuerpo para poder inspeccionar la salida.
        var salida = new MemoryStream();
        var respFeature = new ResponseFeatureConCallbacks { Body = salida };
        ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(respFeature);
        ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>(
            new Microsoft.AspNetCore.Http.StreamResponseBodyFeature(salida));

        var mw = new CacheHttpMiddleware(async innerCtx =>
        {
            innerCtx.Response.StatusCode = statusCode;
            if (cacheControlEndpoint is not null)
                innerCtx.Response.Headers[HeaderNames.CacheControl] = cacheControlEndpoint;
            if (!string.IsNullOrEmpty(cuerpo) && statusCode != StatusCodes.Status204NoContent)
            {
                innerCtx.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(cuerpo);
                await innerCtx.Response.Body.WriteAsync(bytes);
            }
        });

        await mw.Invoke(ctx);
        // Dispara los callbacks OnStarting (en la ruta con ETag ya se enviaron las cabeceras).
        await respFeature.FireOnStarting();
        return ctx;
    }

    private static string? CacheControl(HttpContext ctx) =>
        ctx.Response.Headers.TryGetValue(HeaderNames.CacheControl, out var v) ? v.ToString() : null;

    [Fact]
    public async Task Por_defecto_toda_ruta_api_es_no_store_con_vary_authorization()
    {
        var ctx = await Ejecutar("GET", "/api/apartados");
        Assert.Equal("no-store", CacheControl(ctx));
        Assert.Contains("Authorization", ctx.Response.Headers[HeaderNames.Vary].ToString());
    }

    [Fact]
    public async Task No_pisa_la_cache_control_que_puso_el_endpoint()
    {
        // VerComprobante pone no-store explícito; el middleware no debe duplicar ni cambiarla.
        var ctx = await Ejecutar(
            "POST", "/api/apartados/a1/abonos/b1/comprobante/ver",
            cacheControlEndpoint: "no-store");
        Assert.Equal("no-store", CacheControl(ctx));
    }

    [Theory]
    [InlineData("/api/productos")]
    [InlineData("/api/productos/p-123")]
    [InlineData("/api/categorias")]
    [InlineData("/api/colores")]
    public async Task Lista_blanca_publica_es_cacheable(string path)
    {
        var ctx = await Ejecutar("GET", path);
        Assert.Equal("public, max-age=30, stale-while-revalidate=120", CacheControl(ctx));
    }

    [Fact]
    public async Task Incluir_inactivos_true_es_private_no_store()
    {
        var ctx = await Ejecutar("GET", "/api/productos", queryIncluirInactivos: "true");
        Assert.Equal("private, no-store", CacheControl(ctx));
    }

    [Fact]
    public async Task Incluir_inactivos_false_sigue_publico()
    {
        var ctx = await Ejecutar("GET", "/api/productos", queryIncluirInactivos: "false");
        Assert.Equal("public, max-age=30, stale-while-revalidate=120", CacheControl(ctx));
    }

    [Fact]
    public async Task Producto_por_codigo_de_barras_no_se_cachea()
    {
        var ctx = await Ejecutar("GET", "/api/productos/codigo/750123456");
        Assert.Equal("no-store", CacheControl(ctx));
    }

    [Fact]
    public async Task Config_es_no_cache()
    {
        var ctx = await Ejecutar("GET", "/api/config");
        Assert.Equal("no-cache", CacheControl(ctx));
    }

    [Theory]
    [InlineData("/api/auth/login", "POST")]
    [InlineData("/api/apartados", "GET")]
    [InlineData("/api/apartados/metricas", "GET")]
    [InlineData("/api/clientas", "GET")]
    [InlineData("/api/personal", "GET")]
    [InlineData("/api/stock/p1/lote", "POST")]
    [InlineData("/api/avisos/c1", "GET")]
    [InlineData("/api/config/datos-bancarios", "GET")]
    public async Task Rutas_sensibles_nunca_se_cachean(string path, string metodo)
    {
        var ctx = await Ejecutar(metodo, path);
        Assert.Equal("no-store", CacheControl(ctx));
    }

    [Fact]
    public async Task Post_a_ruta_de_lista_blanca_no_se_cachea()
    {
        // Solo GET entra a la lista blanca; un POST a /api/productos cae al no-store por defecto.
        var ctx = await Ejecutar("POST", "/api/productos");
        Assert.Equal("no-store", CacheControl(ctx));
    }

    [Fact]
    public async Task Lista_blanca_calcula_etag_debil()
    {
        var ctx = await Ejecutar("GET", "/api/productos", cuerpo: "[{\"id\":\"p1\"}]");
        var etag = ctx.Response.Headers[HeaderNames.ETag].ToString();
        Assert.StartsWith("W/\"", etag);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task If_none_match_coincidente_devuelve_304_sin_cuerpo()
    {
        const string cuerpo = "[{\"id\":\"p1\"}]";
        // Primera llamada: obtenemos el ETag.
        var primera = await Ejecutar("GET", "/api/productos", cuerpo: cuerpo);
        var etag = primera.Response.Headers[HeaderNames.ETag].ToString();

        // Segunda con ese ETag en If-None-Match: 304 y cuerpo vacío.
        var segunda = await Ejecutar("GET", "/api/productos", ifNoneMatch: etag, cuerpo: cuerpo);
        Assert.Equal(StatusCodes.Status304NotModified, segunda.Response.StatusCode);
        segunda.Response.Body.Position = 0;
        var leido = new StreamReader(segunda.Response.Body).ReadToEnd();
        Assert.Equal("", leido);
        // Conserva ETag y Cache-Control en el 304.
        Assert.Equal(etag, segunda.Response.Headers[HeaderNames.ETag].ToString());
        Assert.Equal("public, max-age=30, stale-while-revalidate=120", CacheControl(segunda));
    }

    [Fact]
    public async Task If_none_match_distinto_devuelve_200_con_cuerpo()
    {
        var ctx = await Ejecutar("GET", "/api/productos", ifNoneMatch: "W/\"deadbeef\"", cuerpo: "[{\"id\":\"p1\"}]");
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        var leido = new StreamReader(ctx.Response.Body).ReadToEnd();
        Assert.Equal("[{\"id\":\"p1\"}]", leido);
    }

    [Fact]
    public async Task Fuera_de_api_no_se_toca()
    {
        var ctx = await Ejecutar("GET", "/swagger/index.html");
        Assert.Null(CacheControl(ctx));
    }

    [Fact]
    public async Task Config_con_304_responde_304()
    {
        const string cuerpo = "{\"suspendida\":false}";
        var primera = await Ejecutar("GET", "/api/config", cuerpo: cuerpo);
        var etag = primera.Response.Headers[HeaderNames.ETag].ToString();
        var segunda = await Ejecutar("GET", "/api/config", ifNoneMatch: etag, cuerpo: cuerpo);
        Assert.Equal(StatusCodes.Status304NotModified, segunda.Response.StatusCode);
        Assert.Equal("no-cache", CacheControl(segunda));
    }
}

/// <summary>
/// <see cref="Microsoft.AspNetCore.Http.Features.IHttpResponseFeature"/> de prueba que SÍ guarda los
/// callbacks OnStarting y los dispara con <see cref="FireOnStarting"/>, imitando lo que hace un
/// servidor real justo antes de enviar las cabeceras. El <see cref="HeaderDictionary"/> es el mismo
/// que ve <c>ctx.Response.Headers</c>, de modo que lo que el middleware escriba se puede leer luego.
/// </summary>
internal sealed class ResponseFeatureConCallbacks : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
{
    private readonly List<(Func<object, Task> cb, object state)> _onStarting = new();
    private bool _disparado;

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;
    public bool HasStarted => _disparado;

    public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));
    public void OnCompleted(Func<object, Task> callback, object state) { }

    public async Task FireOnStarting()
    {
        if (_disparado) return;
        _disparado = true;
        foreach (var (cb, state) in _onStarting)
            await cb(state);
    }
}

