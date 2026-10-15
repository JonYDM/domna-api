using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Pruebas de integración HTTP sobre el pipeline real (WebApplicationFactory). Verifican que el
/// orden de middlewares es correcto: compresión + CORS + caché + manejador de errores conviven.
/// Cubren principalmente que la compresión NO se aplica bajo /api/auth/* (precaución BREACH) y que
/// las cabeceras de caché salen de punta a punta. La app corre con InMemory (sin DATABASE_URL).
/// </summary>
public class PipelineCacheTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PipelineCacheTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Cliente()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Pedimos explícitamente compresión para comprobar si el servidor la aplica o no.
        c.DefaultRequestHeaders.AcceptEncoding.ParseAdd("br, gzip");
        return c;
    }

    [Fact]
    public async Task Catalogo_publico_sale_cacheable_de_punta_a_punta()
    {
        var r = await Cliente().GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotNull(r.Headers.CacheControl);
        Assert.True(r.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(30), r.Headers.CacheControl.MaxAge);
        Assert.NotNull(r.Headers.ETag);
    }

    [Fact]
    public async Task Config_sale_no_cache()
    {
        var r = await Cliente().GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(r.Headers.CacheControl!.NoCache);
    }

    [Fact]
    public async Task Etag_condicional_devuelve_304()
    {
        var cliente = Cliente();
        var primera = await cliente.GetAsync("/api/productos");
        var etag = primera.Headers.ETag!;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/productos");
        req.Headers.IfNoneMatch.Add(etag);
        var segunda = await cliente.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotModified, segunda.StatusCode);
        var cuerpo = await segunda.Content.ReadAsByteArrayAsync();
        Assert.Empty(cuerpo);
    }

    [Fact]
    public async Task Compresion_no_se_aplica_bajo_api_auth()
    {
        var cliente = Cliente();

        // Control positivo: un GET público NO-auth (catálogo) SÍ se comprime (br o gzip).
        var catalogo = await cliente.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.OK, catalogo.StatusCode);
        Assert.True(
            catalogo.Content.Headers.ContentEncoding.Any(e => e is "br" or "gzip"),
            "El catálogo debería comprimirse (control positivo de la compresión).");

        // /api/auth/existe es un GET público; aunque pidamos br/gzip NO debe venir comprimido
        // (precaución BREACH: bajo /api/auth viajan tokens).
        var auth = await cliente.GetAsync("/api/auth/existe?telefono=5550000000");
        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
        Assert.Empty(auth.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Ruta_sensible_sale_no_store()
    {
        // Sin token: la respuesta (401) también debe quedar bajo el no-store por defecto.
        var r = await Cliente().GetAsync("/api/apartados/metricas");
        Assert.NotNull(r.Headers.CacheControl);
        Assert.True(r.Headers.CacheControl!.NoStore);
    }
}
