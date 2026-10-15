using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Domna.Infrastructure.Persistence;
using Domna.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Sesión única y límite de intentos PROBADOS DE PUNTA A PUNTA por HTTP, con el pipeline real
/// (JWT bearer, eventos, caché, manejador de errores). Las pruebas unitarias del servicio no
/// detectan, por ejemplo, un token que el servidor deja pasar por una regla de la autenticación.
/// Cada prueba levanta su propia app (InMemory con el seed de demo: María, 777 123 4567 / 123456),
/// así los contadores y las sesiones de una no afectan a otra.
/// </summary>
public class SesionHttpTests
{
    private const string Telefono = "7771234567";
    private const string Pin = "123456";

    /// <summary>
    /// Una app nueva con SU PROPIA base en memoria. Sin esto, todas las apps de prueba del proceso
    /// comparten la misma base ("domna"): las sesiones y los intentos de una prueba se filtran a otra
    /// y el seed se repite.
    /// </summary>
    private static WebApplicationFactory<Program> NuevaApp()
    {
        var nombreBase = $"prueba-{Guid.NewGuid():N}"; // una vez por app, no por petición
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(servicios =>
        {
            var existente = servicios.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<DomnaDbContext>));
            if (existente is not null) servicios.Remove(existente);
            servicios.AddDbContext<DomnaDbContext>(o => o.UseInMemoryDatabase(nombreBase));
        }));
    }

    private static async Task<HttpResponseMessage> Login(HttpClient c, string pin = Pin, bool reemplazar = false) =>
        await c.PostAsJsonAsync("/api/auth/login", new { telefono = Telefono, pin, reemplazarSesion = reemplazar });

    private static async Task<string> Token(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> Sesion(HttpClient c, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/sesion");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await c.SendAsync(req);
    }

    private static async Task<string?> Codigo(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("codigo", out var c) ? c.GetString() : null;
    }

    // ── Cerrar la sesión de verdad ──

    [Fact]
    public async Task Al_reemplazar_la_sesion_el_token_viejo_deja_de_servir_de_inmediato()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();

        var viejo = await Token(await Login(c));
        Assert.Equal(HttpStatusCode.OK, (await Sesion(c, viejo)).StatusCode);

        // Un segundo login sin reemplazar NO entra y NO toca la sesión vieja.
        var conflicto = await Login(c);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Equal("sesion_activa", await Codigo(conflicto));
        Assert.Equal(HttpStatusCode.OK, (await Sesion(c, viejo)).StatusCode);

        // Con reemplazar: entra y la vieja muere.
        var nuevo = await Token(await Login(c, reemplazar: true));
        var tras = await Sesion(c, viejo);
        Assert.Equal(HttpStatusCode.Unauthorized, tras.StatusCode);
        Assert.Equal("sesion_reemplazada", await Codigo(tras));
        Assert.Equal(HttpStatusCode.OK, (await Sesion(c, nuevo)).StatusCode);
    }

    [Fact]
    public async Task El_token_viejo_tambien_muere_en_otras_rutas()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();
        var viejo = await Token(await Login(c));
        await Token(await Login(c, reemplazar: true));

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/apartados/clienta/c-maria");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viejo);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Cerrar_sesion_la_invalida_en_el_servidor_y_es_idempotente()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();
        var token = await Token(await Login(c));

        HttpRequestMessage Logout() =>
            new(HttpMethod.Post, "/api/auth/logout") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(Logout())).StatusCode);
        var tras = await Sesion(c, token);
        Assert.Equal(HttpStatusCode.Unauthorized, tras.StatusCode);
        Assert.Equal("sesion_cerrada", await Codigo(tras));

        // Y ya puede entrar otra vez sin conflicto.
        Assert.Equal(HttpStatusCode.OK, (await Login(c)).StatusCode);
    }

    [Fact]
    public async Task Un_token_sin_id_de_sesion_ya_no_se_acepta()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();
        var opts = app.Services.GetRequiredService<JwtOptions>();

        // Como los tokens emitidos ANTES de la sesión única: firma y fecha correctas, pero sin `sid`.
        var llave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));
        var jwt = new JwtSecurityToken(
            opts.Issuer,
            opts.Audience,
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "c-maria"),
                new Claim(ClaimTypes.Role, "clienta"),
            },
            expires: DateTime.UtcNow.AddDays(3),
            signingCredentials: new SigningCredentials(llave, SecurityAlgorithms.HmacSha256));
        var viejo = new JwtSecurityTokenHandler().WriteToken(jwt);

        var r = await Sesion(c, viejo);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("sesion_expirada", await Codigo(r));

        // Y tampoco sirve para rutas que antes dejaba pasar.
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/apartados/clienta/c-maria");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viejo);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Un_token_con_id_de_una_sesion_inexistente_no_pasa()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();
        var opts = app.Services.GetRequiredService<JwtOptions>();
        var llave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));
        var jwt = new JwtSecurityToken(
            opts.Issuer,
            opts.Audience,
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "c-maria"),
                new Claim(ClaimTypes.Role, "clienta"),
                new Claim("sid", "s-inventada"),
            },
            expires: DateTime.UtcNow.AddDays(3),
            signingCredentials: new SigningCredentials(llave, SecurityAlgorithms.HmacSha256));

        var r = await Sesion(c, new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Sin_token_responde_401_con_codigo()
    {
        using var app = NuevaApp();
        var r = await app.CreateClient().GetAsync("/api/auth/sesion");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("no_autenticado", await Codigo(r));
    }

    // ── Límite de intentos de PIN ──

    [Fact]
    public async Task Tras_cinco_pines_incorrectos_ni_el_correcto_entra()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, pin: "000000")).StatusCode);

        var bloqueado = await Login(c); // PIN correcto, pero ya está bloqueado
        Assert.Equal((HttpStatusCode)429, bloqueado.StatusCode);
        Assert.Equal("demasiados_intentos", await Codigo(bloqueado));
    }

    [Fact]
    public async Task Cuatro_fallos_no_bloquean_y_un_acierto_borra_la_cuenta()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();

        for (var i = 0; i < 4; i++) await Login(c, pin: "000000");
        var ok = await Login(c);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await c.PostAsync("/api/auth/logout", null); // sin token: solo para no dejar sesión; el 401 es irrelevante

        // El acierto reinició el contador: otros 4 fallos siguen sin bloquear.
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, pin: "111111")).StatusCode);
    }

    [Fact]
    public async Task Un_telefono_que_no_existe_se_bloquea_igual_y_no_revela_nada()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();

        HttpResponseMessage Intento() => c.PostAsJsonAsync("/api/auth/login", new { telefono = "7770000999", pin = "000000" }).Result;
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, Intento().StatusCode);
        Assert.Equal((HttpStatusCode)429, Intento().StatusCode);
    }

    [Fact]
    public async Task El_bloqueo_de_un_telefono_no_afecta_a_otro()
    {
        using var app = NuevaApp();
        var c = app.CreateClient();
        for (var i = 0; i < 6; i++) await c.PostAsJsonAsync("/api/auth/login", new { telefono = "7770000999", pin = "000000" });

        Assert.Equal(HttpStatusCode.OK, (await Login(c)).StatusCode);
    }
}
