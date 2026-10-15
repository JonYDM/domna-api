using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class SesionServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly SesionRepoFake _repo = new();
    private readonly CacheSesionFake _cache = new();

    private SesionService Crear() => new(_repo, _reloj, _cache);

    private DateTime Expira => _reloj.Ahora.AddDays(7);

    [Fact]
    public async Task Crear_persiste_una_sesion_activa()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "Android · Chrome", Expira);

        var activas = await svc.ListarActivasAsync("u-1", _reloj.Ahora);
        Assert.Single(activas);
        Assert.Equal("Android · Chrome", activas[0].Dispositivo);
    }

    [Fact]
    public async Task Validar_sesion_activa()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "Windows · Edge", Expira);

        Assert.Equal(EstadoSesion.Activa, await svc.ValidarAsync("s-1", _reloj.Ahora));
    }

    [Fact]
    public async Task Validar_sid_desconocido_es_invalida()
    {
        var svc = Crear();
        Assert.Equal(EstadoSesion.Invalida, await svc.ValidarAsync("no-existe", _reloj.Ahora));
    }

    [Fact]
    public async Task Validar_sesion_expirada()
    {
        var svc = Crear();
        // Vence hace un segundo.
        await svc.CrearAsync("s-1", "u-1", "Windows · Edge", _reloj.Ahora.AddSeconds(-1));

        Assert.Equal(EstadoSesion.Expirada, await svc.ValidarAsync("s-1", _reloj.Ahora));
    }

    [Theory]
    [InlineData(MotivosRevocacion.Reemplazada, EstadoSesion.Reemplazada)]
    [InlineData(MotivosRevocacion.Cerrada, EstadoSesion.Cerrada)]
    [InlineData(MotivosRevocacion.PinCambiado, EstadoSesion.PinCambiado)]
    [InlineData(MotivosRevocacion.Desactivada, EstadoSesion.Desactivada)]
    public async Task Validar_sesion_revocada_devuelve_el_estado_por_motivo(string motivo, EstadoSesion esperado)
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "Windows · Edge", Expira);
        await svc.RevocarAsync("s-1", motivo, _reloj.Ahora);

        Assert.Equal(esperado, await svc.ValidarAsync("s-1", _reloj.Ahora));
    }

    [Fact]
    public async Task Revocar_es_idempotente_y_conserva_el_primer_motivo()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "Windows · Edge", Expira);

        await svc.RevocarAsync("s-1", MotivosRevocacion.Cerrada, _reloj.Ahora);
        // Segundo intento con otro motivo no cambia nada ni lanza.
        await svc.RevocarAsync("s-1", MotivosRevocacion.Reemplazada, _reloj.Ahora);

        Assert.Equal(EstadoSesion.Cerrada, await svc.ValidarAsync("s-1", _reloj.Ahora));
    }

    [Fact]
    public async Task Revocar_sid_inexistente_no_lanza()
    {
        var svc = Crear();
        await svc.RevocarAsync("no-existe", MotivosRevocacion.Cerrada, _reloj.Ahora);
        // No se esperan excepciones.
    }

    [Fact]
    public async Task RevocarTodas_revoca_todas_las_activas_del_usuario()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "A", Expira);
        await svc.CrearAsync("s-2", "u-1", "B", Expira);
        await svc.CrearAsync("s-3", "u-otro", "C", Expira);

        var n = await svc.RevocarTodasAsync("u-1", MotivosRevocacion.PinCambiado, _reloj.Ahora);

        Assert.Equal(2, n);
        Assert.Empty(await svc.ListarActivasAsync("u-1", _reloj.Ahora));
        Assert.Single(await svc.ListarActivasAsync("u-otro", _reloj.Ahora)); // la de otro usuario sigue
    }

    [Fact]
    public async Task Revocar_invalida_la_entrada_de_cache()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "A", Expira);

        await svc.RevocarAsync("s-1", MotivosRevocacion.Cerrada, _reloj.Ahora);

        Assert.Contains("s-1", _cache.Invalidados);
    }

    [Fact]
    public async Task Tocar_no_actualiza_mas_de_una_vez_por_minuto()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "A", Expira); // UltimaActividad = _reloj.Ahora

        // A los 30 s: NO toca (menos de un minuto).
        Assert.False(await svc.TocarAsync("s-1", _reloj.Ahora.AddSeconds(30)));

        // A los 61 s: SÍ toca.
        Assert.True(await svc.TocarAsync("s-1", _reloj.Ahora.AddSeconds(61)));

        // Justo después, otros 30 s: NO vuelve a tocar.
        Assert.False(await svc.TocarAsync("s-1", _reloj.Ahora.AddSeconds(61 + 30)));
    }

    [Fact]
    public async Task ObtenerActual_devuelve_los_datos_de_la_sesion()
    {
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "iPhone · Safari", Expira);

        var actual = await svc.ObtenerActualAsync("s-1");

        Assert.NotNull(actual);
        Assert.Equal("iPhone · Safari", actual!.Dispositivo);
        Assert.Equal(_reloj.Ahora, actual.Creada);
        Assert.Equal(Expira, actual.Expira);
    }

    [Fact]
    public async Task Logout_revoca_la_sesion_actual_y_es_idempotente()
    {
        // Replica lo que hace AuthController.Logout: RevocarAsync(sid, "cerrada", ...).
        var svc = Crear();
        await svc.CrearAsync("s-1", "u-1", "A", Expira);

        await svc.RevocarAsync("s-1", MotivosRevocacion.Cerrada, _reloj.Ahora);
        Assert.Equal(EstadoSesion.Cerrada, await svc.ValidarAsync("s-1", _reloj.Ahora));

        // Segundo logout sobre la misma sesión: no lanza y sigue "cerrada" (idempotente ⇒ 204).
        await svc.RevocarAsync("s-1", MotivosRevocacion.Cerrada, _reloj.Ahora);
        Assert.Equal(EstadoSesion.Cerrada, await svc.ValidarAsync("s-1", _reloj.Ahora));
    }
}

public class DescripcionDispositivoTests
{
    [Theory]
    // Chrome en Android
    [InlineData("Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36", "Android · Chrome")]
    // Safari en iPhone
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1", "iPhone · Safari")]
    // Edge en Windows (incluye Chrome/Safari en el UA, pero gana Edg)
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0", "Windows · Edge")]
    // Chrome en Windows
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "Windows · Chrome")]
    // Firefox en Windows
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0", "Windows · Firefox")]
    // Safari en Mac
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15", "Mac · Safari")]
    // Firefox en iOS (FxiOS) sobre iPhone
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/121.0 Mobile/15E148 Safari/605.1.15", "iPhone · Firefox")]
    public void De_reconoce_user_agents_reales(string ua, string esperado)
    {
        Assert.Equal(esperado, DescripcionDispositivo.De(ua));
    }

    [Fact]
    public void De_vacio_devuelve_otro_dispositivo()
    {
        Assert.Equal("Otro dispositivo", DescripcionDispositivo.De(""));
        Assert.Equal("Otro dispositivo", DescripcionDispositivo.De("   "));
    }

    [Fact]
    public void De_nulo_devuelve_otro_dispositivo()
    {
        Assert.Equal("Otro dispositivo", DescripcionDispositivo.De(null));
    }

    [Fact]
    public void De_desconocido_devuelve_otro_dispositivo()
    {
        Assert.Equal("Otro dispositivo", DescripcionDispositivo.De("curl/8.4.0"));
    }
}
