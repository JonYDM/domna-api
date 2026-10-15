using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class AuthServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly HasherFake _hasher = new();
    private readonly JwtFake _jwt = new();
    private readonly CorreoFake _correo = new();

    private AuthService Crear(ClientaRepoFake repo, SesionRepoFake? sesiones = null)
    {
        var sesionSvc = new SesionService(sesiones ?? new SesionRepoFake(), _reloj, new CacheSesionFake());
        return new(repo, _hasher, _jwt, _correo, _reloj, sesionSvc, new LimitadorIntentos(_reloj));
    }

    private static RegistroClientaInput RegistroMaria(string pin = "123456") =>
        new("María", "González", "7771234567", "maria@correo.com", pin);

    [Fact]
    public async Task Registrar_crea_clienta_y_devuelve_sesion()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);

        var sesion = await auth.RegistrarAsync(RegistroMaria());

        Assert.False(string.IsNullOrEmpty(sesion.Token));
        Assert.Equal("777 123 4567", sesion.Clienta.Telefono);
        Assert.Equal("maria@correo.com", sesion.Clienta.Email);
        Assert.Equal("registro", sesion.Clienta.Origen);
    }

    [Fact]
    public async Task Registrar_rechaza_telefono_duplicado_con_cuenta()
    {
        var existente = Clienta.Crear("c1", "Ana", "Ruiz", "7771234567", "ana@correo.com",
            OrigenClienta.Registro, _reloj.Ahora, _hasher.Hash("999999"));
        var repo = new ClientaRepoFake(existente);
        var auth = Crear(repo);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => auth.RegistrarAsync(RegistroMaria()));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Registrar_reclama_ficha_de_mostrador_sin_pin()
    {
        // Alta en mostrador SIN PIN (sin acceso).
        var mostrador = Clienta.Crear("c1", "Clienta Mostrador", "", "7771234567", "temp@correo.com",
            OrigenClienta.Mostrador, _reloj.Ahora, pinHash: null);
        var repo = new ClientaRepoFake(mostrador);
        var auth = Crear(repo);

        var sesion = await auth.RegistrarAsync(RegistroMaria());

        // Conserva el id (historial) pero ahora es registro con acceso.
        Assert.Equal("c1", sesion.Clienta.Id);
        Assert.Equal("registro", sesion.Clienta.Origen);
        Assert.Equal("María", sesion.Clienta.Nombre);
    }

    [Fact]
    public async Task Login_con_pin_correcto_devuelve_sesion()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);
        await auth.RegistrarAsync(RegistroMaria());

        // El registro ya dejó una sesión activa; para iniciar en "otro dispositivo" pedimos reemplazo.
        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "123456", ReemplazarSesion: true));
        Assert.Equal("777 123 4567", sesion.Clienta.Telefono);
    }

    [Fact]
    public async Task Login_con_pin_incorrecto_lanza_401()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);
        await auth.RegistrarAsync(RegistroMaria());

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.LoginAsync(new LoginInput("7771234567", "000000")));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Login_telefono_inexistente_lanza_401_generico()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.LoginAsync(new LoginInput("7779999999", "123456")));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Recuperacion_flujo_completo_fija_pin_nuevo()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);
        await auth.RegistrarAsync(RegistroMaria());

        // Solicitar: devuelve token y lo "envía" por correo.
        var rec = await auth.SolicitarRecuperacionAsync(new SolicitarRecuperacionInput("maria@correo.com"));
        Assert.NotNull(rec.Token);
        Assert.Equal(rec.Token, _correo.UltimoToken);

        // Confirmar con PIN nuevo.
        await auth.ConfirmarRecuperacionAsync(new ConfirmarRecuperacionInput(rec.Token!, "654321"));

        // El PIN viejo ya no sirve; el nuevo sí.
        await Assert.ThrowsAsync<DomnaException>(() => auth.LoginAsync(new LoginInput("7771234567", "123456")));
        // Confirmar la recuperación dejó una sesión activa; pedimos reemplazo para el login nuevo.
        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "654321", ReemplazarSesion: true));
        Assert.Equal("c-", sesion.Clienta.Id[..2]);
    }

    [Fact]
    public async Task Recuperacion_correo_inexistente_no_revela_y_devuelve_token_null()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);

        var rec = await auth.SolicitarRecuperacionAsync(new SolicitarRecuperacionInput("nadie@correo.com"));
        Assert.Null(rec.Token);
    }

    [Fact]
    public async Task Recuperacion_token_un_solo_uso()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);
        await auth.RegistrarAsync(RegistroMaria());
        var rec = await auth.SolicitarRecuperacionAsync(new SolicitarRecuperacionInput("maria@correo.com"));

        await auth.ConfirmarRecuperacionAsync(new ConfirmarRecuperacionInput(rec.Token!, "654321"));
        // Segundo intento con el mismo token falla.
        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.ConfirmarRecuperacionAsync(new ConfirmarRecuperacionInput(rec.Token!, "111111")));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ExisteTelefono_true_si_tiene_cuenta_con_pin()
    {
        var repo = new ClientaRepoFake();
        var auth = Crear(repo);
        await auth.RegistrarAsync(RegistroMaria());

        Assert.True(await auth.ExisteTelefonoAsync("7771234567"));
        // Acepta distintos formatos del mismo número.
        Assert.True(await auth.ExisteTelefonoAsync("+52 777 123 4567"));
    }

    [Fact]
    public async Task ExisteTelefono_false_si_no_hay_cuenta()
    {
        var auth = Crear(new ClientaRepoFake());
        Assert.False(await auth.ExisteTelefonoAsync("7779998877"));
    }

    [Fact]
    public async Task ExisteTelefono_false_si_telefono_mal_formado_no_lanza()
    {
        var auth = Crear(new ClientaRepoFake());
        // No debe lanzar 400: devuelve false mientras el usuario aún escribe.
        Assert.False(await auth.ExisteTelefonoAsync("123"));
    }

    [Fact]
    public async Task ExisteTelefono_false_para_ficha_de_mostrador_sin_pin()
    {
        var mostrador = Clienta.Crear("c1", "Mostrador", "", "7771234567", "m@correo.com",
            OrigenClienta.Mostrador, _reloj.Ahora, pinHash: null);
        var auth = Crear(new ClientaRepoFake(mostrador));
        // Existe la ficha pero no tiene acceso (sin PIN): no puede iniciar sesión.
        Assert.False(await auth.ExisteTelefonoAsync("7771234567"));
    }

    [Fact]
    public async Task Login_de_staff_devuelve_su_rol()
    {
        var jessica = Clienta.CrearStaff("u1", "Jessica", "Jesly", "7774602303",
            "jessica@correo.com", Rol.Duena, _hasher.Hash("010101"), _reloj.Ahora);
        var auth = Crear(new ClientaRepoFake(jessica));

        var sesion = await auth.LoginAsync(new LoginInput("7774602303", "010101"));
        Assert.Equal("duena", sesion.Clienta.Rol);
    }

    [Fact]
    public async Task Login_de_cajera_incluye_su_sucursal()
    {
        var caja = Clienta.CrearStaff("u2", "Caja", "", "7771004050",
            "caja@correo.com", Rol.Cajera, _hasher.Hash("135790"), _reloj.Ahora, SucursalId.Temixco);
        var auth = Crear(new ClientaRepoFake(caja));

        var sesion = await auth.LoginAsync(new LoginInput("7771004050", "135790"));
        Assert.Equal("cajera", sesion.Clienta.Rol);
        Assert.Equal("temixco", sesion.Clienta.SucursalCajera);
    }

    [Fact]
    public async Task Login_de_cajera_desactivada_lanza_401_generico()
    {
        var caja = Clienta.CrearStaff("u2", "Caja", "", "7771004050",
            "caja@correo.com", Rol.Cajera, _hasher.Hash("135790"), _reloj.Ahora, SucursalId.Temixco);
        caja.Desactivar();
        var auth = Crear(new ClientaRepoFake(caja));

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.LoginAsync(new LoginInput("7771004050", "135790")));
        Assert.Equal(401, ex.StatusCode);
    }

    // ───────────────────────── Sesión única (una sola sesión activa) ─────────────────────────

    [Fact]
    public async Task Login_sin_sesion_previa_crea_una_sesion()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        await auth.RegistrarAsync(RegistroMaria()); // registrar también crea sesión

        // Simulamos "otro dispositivo": cerramos la del registro para que el login quede sin previa.
        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        await svc.RevocarTodasAsync(sesiones.Todas[0].UsuarioId, MotivosRevocacion.Cerrada, _reloj.Ahora);

        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "123456"));

        Assert.False(string.IsNullOrEmpty(sesion.Token));
        // Hay exactamente una sesión activa.
        var activas = await svc.ListarActivasAsync(sesiones.Todas[0].UsuarioId, _reloj.Ahora);
        Assert.Single(activas);
    }

    [Fact]
    public async Task Registrar_crea_sesion_activa()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);

        await auth.RegistrarAsync(RegistroMaria());

        Assert.Single(sesiones.Todas);
        Assert.False(sesiones.Todas[0].Revocada);
    }

    [Fact]
    public async Task El_aviso_distingue_la_app_instalada_del_navegador_en_el_mismo_telefono()
    {
        const string ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1";
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        // Primero entra desde Safari...
        await auth.RegistrarAsync(RegistroMaria(), ua);

        // ...y luego abre la app instalada en el MISMO iPhone: el aviso debe nombrar a Safari, no repetir a la app.
        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.LoginAsync(new LoginInput("7771234567", "123456"), ua, default, appInstalada: true));

        Assert.Equal("sesion_activa", ex.Codigo);
        Assert.Equal("iPhone · Safari", ex.Datos!.GetType().GetProperty("dispositivo")!.GetValue(ex.Datos));
    }

    [Fact]
    public async Task Al_reemplazar_desde_la_app_instalada_la_sesion_nueva_queda_como_app_instalada()
    {
        const string ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1";
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        await auth.RegistrarAsync(RegistroMaria(), ua);

        await auth.LoginAsync(new LoginInput("7771234567", "123456", true), ua, default, appInstalada: true);

        Assert.Equal("iPhone · App instalada", sesiones.Todas.Single(s => !s.Revocada).Dispositivo);
        // Y la otra (Safari) quedó cerrada.
        Assert.Contains(sesiones.Todas, s => s.Revocada && s.Dispositivo == "iPhone · Safari");
    }

    [Fact]
    public async Task Login_con_sesion_activa_devuelve_409_con_codigo_y_datos()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        // Registro deja una sesión activa (dispositivo "Android · Chrome").
        await auth.RegistrarAsync(RegistroMaria(), "Mozilla/5.0 (Linux; Android 13) Chrome/120.0 Mobile Safari/537.36");

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.LoginAsync(new LoginInput("7771234567", "123456")));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("sesion_activa", ex.Codigo);
        Assert.Equal("Ya tienes una sesion abierta en otro dispositivo.", ex.Message);
        Assert.NotNull(ex.Datos);
        // Los datos traen dispositivo, creada y ultimaActividad.
        var tipo = ex.Datos!.GetType();
        Assert.Equal("Android · Chrome", tipo.GetProperty("dispositivo")!.GetValue(ex.Datos));
        Assert.NotNull(tipo.GetProperty("creada")!.GetValue(ex.Datos));
        Assert.NotNull(tipo.GetProperty("ultimaActividad")!.GetValue(ex.Datos));
        // No se creó una segunda sesión ni se tocó la primera.
        Assert.Single(sesiones.Todas);
        Assert.False(sesiones.Todas[0].Revocada);
    }

    [Fact]
    public async Task Login_con_reemplazarSesion_revoca_la_otra_y_crea_la_nueva()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        await auth.RegistrarAsync(RegistroMaria());
        var usuarioId = sesiones.Todas[0].UsuarioId;

        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "123456", ReemplazarSesion: true));

        Assert.False(string.IsNullOrEmpty(sesion.Token));
        // La primera quedó revocada con motivo "reemplazada"; hay una sola activa.
        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        var activas = await svc.ListarActivasAsync(usuarioId, _reloj.Ahora);
        Assert.Single(activas);
        Assert.Contains(sesiones.Todas, s => s.Revocada && s.MotivoRevocacion == MotivosRevocacion.Reemplazada);
    }

    [Fact]
    public async Task Login_con_pin_incorrecto_no_revela_sesion_abierta()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        await auth.RegistrarAsync(RegistroMaria()); // deja una sesión activa

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => auth.LoginAsync(new LoginInput("7771234567", "000000")));

        // Debe ser el 401 genérico de siempre, NO el 409 de sesión activa.
        Assert.Equal(401, ex.StatusCode);
        Assert.Null(ex.Codigo);
    }

    [Fact]
    public async Task Login_con_sesion_expirada_no_bloquea_un_login_nuevo()
    {
        // Clienta ya registrada, con UNA sesión previa YA VENCIDA (expira ayer).
        var clienta = Clienta.Crear("c1", "María", "González", "7771234567", "maria@correo.com",
            OrigenClienta.Registro, _reloj.Ahora, _hasher.Hash("123456"));
        var expirada = Sesion.Crear("s-vieja", clienta.Id, "Android · Chrome",
            _reloj.Ahora.AddDays(-10), _reloj.Ahora.AddDays(-3));
        var repo = new ClientaRepoFake(clienta);
        var sesiones = new SesionRepoFake(expirada);
        var auth = Crear(repo, sesiones);

        // Login sin reemplazar: no debe dar 409 porque la única sesión previa está vencida.
        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "123456"));
        Assert.False(string.IsNullOrEmpty(sesion.Token));

        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        Assert.Single(await svc.ListarActivasAsync(clienta.Id, _reloj.Ahora));
    }

    [Fact]
    public async Task Dos_logins_con_reemplazo_en_cadena_dejan_una_sola_activa()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        await auth.RegistrarAsync(RegistroMaria());
        var usuarioId = sesiones.Todas[0].UsuarioId;

        await auth.LoginAsync(new LoginInput("7771234567", "123456", ReemplazarSesion: true));
        await auth.LoginAsync(new LoginInput("7771234567", "123456", ReemplazarSesion: true));

        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        var activas = await svc.ListarActivasAsync(usuarioId, _reloj.Ahora);
        Assert.Single(activas);
        // Hubo 3 sesiones en total (registro + 2 logins), 2 revocadas.
        Assert.Equal(3, sesiones.Todas.Count);
        Assert.Equal(2, sesiones.Todas.Count(s => s.Revocada));
    }

    [Fact]
    public async Task Recuperacion_confirmar_revoca_todas_las_sesiones()
    {
        var repo = new ClientaRepoFake();
        var sesiones = new SesionRepoFake();
        var auth = Crear(repo, sesiones);
        await auth.RegistrarAsync(RegistroMaria());
        var usuarioId = sesiones.Todas[0].UsuarioId;

        var rec = await auth.SolicitarRecuperacionAsync(new SolicitarRecuperacionInput("maria@correo.com"));
        await auth.ConfirmarRecuperacionAsync(new ConfirmarRecuperacionInput(rec.Token!, "654321"));

        // Las sesiones previas quedaron revocadas por "pin_cambiado"; la confirmación crea una nueva.
        Assert.Contains(sesiones.Todas, s => s.Revocada && s.MotivoRevocacion == MotivosRevocacion.PinCambiado);
        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        Assert.Single(await svc.ListarActivasAsync(usuarioId, _reloj.Ahora));
    }
}
