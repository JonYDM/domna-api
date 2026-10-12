using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class PersonalServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly HasherFake _hasher = new();

    private PersonalService Crear(ClientaRepoFake repo) => new(repo, _hasher, _reloj);

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
}
