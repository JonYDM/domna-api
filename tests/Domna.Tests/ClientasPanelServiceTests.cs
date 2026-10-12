using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class ClientasPanelServiceTests
{
    private readonly RelojFijo _reloj = new();

    private ClientasPanelService Crear(ClientaRepoFake repo) => new(repo, _reloj);

    private static Clienta ClientaDe(string id, string nombre, string telefono, string email, DateTime creada, OrigenClienta origen = OrigenClienta.Registro) =>
        Clienta.Crear(id, nombre, "Pérez", telefono, email, origen, creada, "hash");

    [Fact]
    public async Task Listar_devuelve_solo_rol_clienta_no_staff()
    {
        var duena = Clienta.CrearStaff("u-jessica", "Jessica", "", "7774602303", "jessica@jeslyboutique.mx", Rol.Duena, "hash", DateTime.UtcNow);
        var maria = ClientaDe("c-maria", "María", "7771234567", "maria@correo.com", _reloj.Ahora);
        var repo = new ClientaRepoFake(duena, maria);

        var lista = await Crear(repo).ListarAsync("todas", null);

        Assert.Single(lista);
        Assert.Equal("María", lista[0].Nombre);
    }

    [Fact]
    public async Task Listar_filtro_nuevas_solo_las_de_los_ultimos_7_dias()
    {
        var reciente = ClientaDe("c1", "Nueva", "7771111111", "nueva@correo.com", _reloj.Ahora.AddDays(-2));
        var vieja = ClientaDe("c2", "Vieja", "7772222222", "vieja@correo.com", _reloj.Ahora.AddDays(-30));
        var repo = new ClientaRepoFake(reciente, vieja);

        var lista = await Crear(repo).ListarAsync("nuevas", null);

        Assert.Single(lista);
        Assert.Equal("Nueva", lista[0].Nombre);
    }

    [Fact]
    public async Task Listar_busca_por_nombre_correo_o_telefono()
    {
        var maria = ClientaDe("c1", "María González", "7771234567", "maria@correo.com", _reloj.Ahora);
        var ana = ClientaDe("c2", "Ana López", "7779998877", "ana@correo.com", _reloj.Ahora);
        var repo = new ClientaRepoFake(maria, ana);
        var svc = Crear(repo);

        Assert.Single(await svc.ListarAsync("todas", "maria"));
        Assert.Single(await svc.ListarAsync("todas", "7779998877"));
        Assert.Empty(await svc.ListarAsync("todas", "nadie"));
    }

    [Fact]
    public async Task Resumen_cuenta_total_nuevas_y_origen()
    {
        var reciente = ClientaDe("c1", "Nueva", "7771111111", "nueva@correo.com", _reloj.Ahora.AddDays(-1), OrigenClienta.Registro);
        var mostrador = ClientaDe("c2", "De mostrador", "7772222222", "mostrador@correo.com", _reloj.Ahora.AddDays(-30), OrigenClienta.Mostrador);
        var repo = new ClientaRepoFake(reciente, mostrador);

        var resumen = await Crear(repo).ResumenAsync();

        Assert.Equal(2, resumen.Total);
        Assert.Equal(1, resumen.NuevasSemana);
        Assert.Equal(1, resumen.PorOrigen["registro"]);
        Assert.Equal(1, resumen.PorOrigen["mostrador"]);
    }

    [Fact]
    public async Task Obtener_devuelve_la_ficha_de_la_clienta()
    {
        var maria = ClientaDe("c1", "María", "7771234567", "maria@correo.com", _reloj.Ahora);
        var repo = new ClientaRepoFake(maria);

        var ficha = await Crear(repo).ObtenerAsync("c1");

        Assert.Equal("María", ficha.Nombre);
    }

    [Fact]
    public async Task Obtener_rechaza_id_de_staff()
    {
        var duena = Clienta.CrearStaff("u-jessica", "Jessica", "", "7774602303", "jessica@jeslyboutique.mx", Rol.Duena, "hash", DateTime.UtcNow);
        var repo = new ClientaRepoFake(duena);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => Crear(repo).ObtenerAsync("u-jessica"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Crear_da_de_alta_en_mostrador_sin_pin()
    {
        var repo = new ClientaRepoFake();
        var svc = Crear(repo);

        var c = await svc.CrearAsync(new CrearClientaInput("Lupita", "Flores", "7779998877", "lupita@correo.com"));

        Assert.Equal("mostrador", c.Origen);
        var guardada = await repo.ObtenerPorIdAsync(c.Id);
        Assert.False(guardada!.TieneAcceso);
    }

    [Fact]
    public async Task Crear_rechaza_telefono_duplicado()
    {
        var existente = ClientaDe("c1", "María", "7771234567", "maria@correo.com", _reloj.Ahora);
        var repo = new ClientaRepoFake(existente);
        var svc = Crear(repo);

        var ex = await Assert.ThrowsAsync<DomnaException>(
            () => svc.CrearAsync(new CrearClientaInput("Otra", "", "7771234567", "otra@correo.com")));
        Assert.Equal(409, ex.StatusCode);
    }
}
