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

    private AuthService Crear(ClientaRepoFake repo) => new(repo, _hasher, _jwt, _correo, _reloj);

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

        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "123456"));
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
        var sesion = await auth.LoginAsync(new LoginInput("7771234567", "654321"));
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
}
