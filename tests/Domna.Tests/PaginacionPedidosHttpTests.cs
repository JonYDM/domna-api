using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Domna.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// La paginación de pedidos de punta a punta: permisos por HTTP y que la consulta se traduzca de verdad a
/// SQL de PostgreSQL (la base en memoria de las otras pruebas acepta consultas que Postgres rechazaría).
/// </summary>
public class PaginacionPedidosHttpTests
{
    private const string TelefonoDuena = "7770009999";
    private const string PinDuena = "246810";

    private static WebApplicationFactory<Program> NuevaApp()
    {
        var nombreBase = $"pag-{Guid.NewGuid():N}";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(servicios =>
        {
            var existente = servicios.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<DomnaDbContext>));
            if (existente is not null) servicios.Remove(existente);
            servicios.AddDbContext<DomnaDbContext>(o => o.UseInMemoryDatabase(nombreBase));
        }));
    }

    /// <summary>Agrega una dueña propia de la prueba (con su PIN) y entra con ella.</summary>
    private static async Task<HttpClient> ComoDuena(WebApplicationFactory<Program> app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DomnaDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Clientas.Add(Clienta.CrearStaff(
                "u-prueba", "Dueña", "Prueba", TelefonoDuena, "duena.prueba@correo.com",
                Rol.Duena, hasher.Hash(PinDuena), DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        return await Entrar(app, TelefonoDuena, PinDuena);
    }

    private static async Task<HttpClient> Entrar(WebApplicationFactory<Program> app, string telefono, string pin)
    {
        var c = app.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { telefono, pin, reemplazarSesion = true });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("token").GetString());
        return c;
    }

    // ── Permisos ──

    [Theory]
    [InlineData("/api/apartados/pagina")]
    [InlineData("/api/apartados/conteos")]
    public async Task Sin_sesion_no_se_puede_ver_la_lista_de_pedidos(string ruta)
    {
        using var app = NuevaApp();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync(ruta)).StatusCode);
    }

    [Theory]
    [InlineData("/api/apartados/pagina")]
    [InlineData("/api/apartados/conteos")]
    public async Task Una_clienta_no_puede_ver_la_lista_de_todos_los_pedidos(string ruta)
    {
        using var app = NuevaApp();
        var maria = await Entrar(app, "7771234567", "123456");
        Assert.Equal(HttpStatusCode.Forbidden, (await maria.GetAsync(ruta)).StatusCode);
    }

    // ── Forma de la respuesta ──

    [Fact]
    public async Task La_duena_recibe_la_pagina_con_total_y_acotada_a_50()
    {
        using var app = NuevaApp();
        var duena = await ComoDuena(app);

        var r = await duena.GetAsync("/api/apartados/pagina?pagina=0&tamano=999&estado=por_vencer&tipo=compras&texto=vestido");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var raiz = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, raiz.GetProperty("items").ValueKind);
        Assert.Equal(0, raiz.GetProperty("total").GetInt32());
        Assert.Equal(1, raiz.GetProperty("pagina").GetInt32());
        Assert.Equal(50, raiz.GetProperty("tamano").GetInt32());
        Assert.False(raiz.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Los_conteos_responden_los_dos_numeros_de_las_pestanas()
    {
        using var app = NuevaApp();
        var duena = await ComoDuena(app);

        var r = await duena.GetAsync("/api/apartados/conteos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("apartadosActivos").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("comprasPorEntregar").GetInt32());
    }

    [Fact]
    public async Task La_lista_completa_de_siempre_sigue_respondiendo_un_arreglo()
    {
        using var app = NuevaApp();
        var duena = await ComoDuena(app);

        var r = await duena.GetAsync("/api/apartados");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    // ── SQL de PostgreSQL ──

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("compras", "por_entregar", null)]
    [InlineData("apartados", "activo", "ana")]
    [InlineData(null, "por_vencer", "DOM-0008")]
    [InlineData("compras", "entregado", "777 123 4567")]
    [InlineData(null, "liquidado", "7771234567")]
    [InlineData(null, "vencido", "vestido rosa")]
    [InlineData(null, "cancelado", "%_")]
    [InlineData(null, "inventado", "x")]
    public void La_consulta_de_una_pagina_se_traduce_a_SQL_de_Postgres(string? tipo, string? estado, string? texto)
    {
        var opciones = new DbContextOptionsBuilder<DomnaDbContext>()
            .UseNpgsql("Host=localhost;Database=prueba;Username=u;Password=p")
            .Options;
        using var db = new DomnaDbContext(opciones);
        var repo = new ApartadoRepository(db);
        var filtro = FiltroPedidos.Combinar(tipo, estado, texto, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        var sql = repo.ConsultaDePagina(filtro, porVencimiento: estado == "por_vencer", pagina: 3, tamano: 20).ToQueryString();

        Assert.Contains("LIMIT", sql);
        Assert.Contains("OFFSET", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public void El_conteo_de_pedidos_se_traduce_a_SQL_de_Postgres()
    {
        var opciones = new DbContextOptionsBuilder<DomnaDbContext>()
            .UseNpgsql("Host=localhost;Database=prueba;Username=u;Password=p")
            .Options;
        using var db = new DomnaDbContext(opciones);
        var filtro = FiltroPedidos.Combinar("compras", "por_entregar", "ana", DateTime.UtcNow);

        var sql = db.Apartados.Where(filtro).ToQueryString();

        Assert.Contains("WHERE", sql);
    }
}
