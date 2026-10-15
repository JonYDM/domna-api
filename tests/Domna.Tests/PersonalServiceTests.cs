using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class PersonalServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly HasherFake _hasher = new();

    private PersonalService Crear(ClientaRepoFake repo, SesionRepoFake? sesiones = null)
    {
        var sesionSvc = new SesionService(sesiones ?? new SesionRepoFake(), _reloj, new CacheSesionFake());
        return new(repo, _hasher, _reloj, sesionSvc);
    }

    private static Clienta Duena() => Clienta.CrearStaff(
        "u-jessica", "Jessica", "Jesly", "7774602303", "jessica@jeslyboutique.mx",
        Rol.Duena, "hash", DateTime.UtcNow);

    private static CrearCajeraInput NuevaCajera(string telefono = "7779998877") =>
        new("Lupita", telefono, "246810", "temixco");

    [Fact]
    public async Task Listar_devuelve_solo_duena_y_cajeras()
    {
        var cajera = Clienta.CrearStaff("u-caja", "Caja Azteca", "", "7770001122", "caja@jeslyboutique.mx",
            Rol.Cajera, "hash", DateTime.UtcNow, SucursalId.Azteca);
        var clienta = Clienta.Crear("c-maria", "María", "González", "7771234567", "maria@correo.com",
            OrigenClienta.Registro, DateTime.UtcNow, "hash");
        var repo = new ClientaRepoFake(Duena(), cajera, clienta);

        var lista = await Crear(repo).ListarAsync();

        Assert.Equal(2, lista.Count);
        Assert.DoesNotContain(lista, c => c.Rol == "clienta");
    }

    [Fact]
    public async Task CrearCajera_crea_con_rol_cajera_y_sucursal()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);

        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        Assert.Equal("cajera", cajera.Rol);
        Assert.Equal("temixco", cajera.SucursalCajera);
        Assert.Equal("777 999 8877", cajera.Telefono);
        var lista = await personal.ListarAsync();
        Assert.Equal(2, lista.Count);
    }

    [Fact]
    public async Task CrearCajera_rechaza_telefono_duplicado()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);

        await Assert.ThrowsAsync<DomnaException>(() => personal.CrearCajeraAsync(NuevaCajera("7774602303")));
    }

    [Fact]
    public async Task CrearCajera_rechaza_pin_invalido()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);

        await Assert.ThrowsAsync<DomnaException>(() => personal.CrearCajeraAsync(new CrearCajeraInput("Lupita", "7779998877", "123", "temixco")));
    }

    [Fact]
    public async Task EditarCajera_actualiza_nombre_telefono_y_sucursal()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);
        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        var editada = await personal.EditarCajeraAsync(cajera.Id, new EditarCajeraInput("Lupita Flores", "7779998800", "azteca"));

        Assert.Equal("Lupita Flores", editada.Nombre);
        Assert.Equal("777 999 8800", editada.Telefono);
        Assert.Equal("azteca", editada.SucursalCajera);
    }

    [Fact]
    public async Task EditarCajera_rechaza_telefono_de_otra_cuenta()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);
        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        await Assert.ThrowsAsync<DomnaException>(
            () => personal.EditarCajeraAsync(cajera.Id, new EditarCajeraInput("Lupita", "7774602303", "temixco")));
    }

    [Fact]
    public async Task EditarCajera_rechaza_editar_a_la_duena()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);

        await Assert.ThrowsAsync<DomnaException>(
            () => personal.EditarCajeraAsync("u-jessica", new EditarCajeraInput("Jessica", "7779998877", "temixco")));
    }

    [Fact]
    public async Task ResetPin_fija_un_pin_nuevo()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);
        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        await personal.ResetPinAsync(cajera.Id, new ResetPinInput("999999"));

        var guardada = await repo.ObtenerPorIdAsync(cajera.Id);
        Assert.True(_hasher.Verificar("999999", guardada!.PinHash!));
    }

    [Fact]
    public async Task Desactivar_y_activar_cambian_el_acceso()
    {
        var repo = new ClientaRepoFake(Duena());
        var personal = Crear(repo);
        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        var desactivada = await personal.DesactivarAsync(cajera.Id);
        Assert.False(desactivada.Activo);

        var reactivada = await personal.ActivarAsync(cajera.Id);
        Assert.True(reactivada.Activo);
    }

    [Fact]
    public async Task ResetPin_revoca_todas_las_sesiones_de_la_cajera()
    {
        var repo = new ClientaRepoFake(Duena());
        var sesiones = new SesionRepoFake();
        var personal = Crear(repo, sesiones);
        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        // La cajera tiene dos sesiones activas.
        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        await svc.CrearAsync("s-1", cajera.Id, "A", _reloj.Ahora.AddDays(7));
        await svc.CrearAsync("s-2", cajera.Id, "B", _reloj.Ahora.AddDays(7));

        await personal.ResetPinAsync(cajera.Id, new ResetPinInput("999999"));

        Assert.Empty(await svc.ListarActivasAsync(cajera.Id, _reloj.Ahora));
        Assert.All(sesiones.Todas, s => Assert.Equal(MotivosRevocacion.PinCambiado, s.MotivoRevocacion));
    }

    [Fact]
    public async Task Desactivar_revoca_todas_las_sesiones_de_la_cajera()
    {
        var repo = new ClientaRepoFake(Duena());
        var sesiones = new SesionRepoFake();
        var personal = Crear(repo, sesiones);
        var cajera = await personal.CrearCajeraAsync(NuevaCajera());

        var svc = new SesionService(sesiones, _reloj, new CacheSesionFake());
        await svc.CrearAsync("s-1", cajera.Id, "A", _reloj.Ahora.AddDays(7));

        await personal.DesactivarAsync(cajera.Id);

        Assert.Empty(await svc.ListarActivasAsync(cajera.Id, _reloj.Ahora));
        Assert.All(sesiones.Todas, s => Assert.Equal(MotivosRevocacion.Desactivada, s.MotivoRevocacion));
    }
}
