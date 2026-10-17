using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// La lista de pedidos de la dueña y la cajera va por páginas: filtra y busca en la base de datos y trae el
/// detalle solo de los pedidos de la página. Aquí se prueba la lógica (páginas, orden, filtros, conteos).
/// </summary>
public class PaginacionPedidosTests
{
    private readonly RelojFijo _reloj = new();
    private readonly ProductoRepoFake _productos = new();
    private readonly ApartadoRepoFake _apartados = new();
    private readonly ClientaRepoFake _clientas;
    private readonly ApartadoService _svc;

    public PaginacionPedidosTests()
    {
        var ana = Clienta.Crear("c1", "Ana", "Lopez", "7771234567", "ana@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        var luz = Clienta.Crear("c2", "Luz", "Pérez", "7779998888", "luz@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        _clientas = new ClientaRepoFake(ana, luz);
        _svc = new ApartadoService(_apartados, _productos, _clientas, new ConfigRepoFake(), _reloj);
    }

    private async Task Prenda(string id, string nombre, int stock = 200)
    {
        var existencias = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { stock } },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = new[] { 0 } },
        };
        var p = Producto.Crear(
            id, nombre, null, "vestidos", Silueta.Vestido, 600m, 250m, true,
            new[] { new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa) },
            new[] { "M" }, existencias, _reloj.Ahora,
            nuevoIdColor: () => $"col-{id}", nuevoIdVariante: () => $"v-{id}");
        await _productos.AgregarAsync(p);
    }

    private static CrearApartadoInput Pedido(string varianteId, string modalidad = "compra", string clienta = "c1") =>
        new(new List<LineaCarritoInput> { new(varianteId, 1) }, modalidad, "temixco", "fisico", clienta, "efectivo");

    /// <summary>Crea n compras, cada una un minuto después de la anterior (la última es la más reciente).</summary>
    private async Task<List<ApartadoDto>> Compras(int n, string variante = "v-p1", string clienta = "c1")
    {
        var creadas = new List<ApartadoDto>();
        for (var i = 0; i < n; i++)
        {
            creadas.Add(await _svc.CrearAsync(clienta, Pedido(variante, "compra", clienta)));
            _reloj.Ahora = _reloj.Ahora.AddMinutes(1);
        }
        return creadas;
    }

    // ── Páginas ──

    [Fact]
    public async Task La_primera_pagina_trae_el_tamano_pedido_el_total_y_avisa_que_hay_mas()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(25);

        var pag = await _svc.BuscarAsync(null, null, null, 1, 10);

        Assert.Equal(10, pag.Items.Count);
        Assert.Equal(25, pag.Total);
        Assert.Equal(1, pag.Pagina);
        Assert.Equal(10, pag.Tamano);
        Assert.True(pag.HayMas);
    }

    [Fact]
    public async Task La_ultima_pagina_trae_lo_que_sobra_y_ya_no_hay_mas()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(25);

        var pag = await _svc.BuscarAsync(null, null, null, 3, 10);

        Assert.Equal(5, pag.Items.Count);
        Assert.False(pag.HayMas);
    }

    [Fact]
    public async Task Cuando_el_total_cae_justo_en_el_limite_de_la_pagina_no_dice_que_hay_mas()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(20);

        var pag = await _svc.BuscarAsync(null, null, null, 2, 10);

        Assert.Equal(10, pag.Items.Count);
        Assert.False(pag.HayMas);
    }

    [Fact]
    public async Task Las_paginas_van_del_mas_reciente_al_mas_viejo_sin_repetir_ni_saltar_pedidos()
    {
        await Prenda("p1", "Vestido rosa");
        var creadas = await Compras(25);

        var vistos = new List<string>();
        for (var p = 1; p <= 3; p++)
            vistos.AddRange((await _svc.BuscarAsync(null, null, null, p, 10)).Items.Select(a => a.Id));

        Assert.Equal(25, vistos.Distinct().Count());
        Assert.Equal(creadas.AsEnumerable().Reverse().Select(a => a.Id), vistos);
    }

    [Fact]
    public async Task Con_la_misma_fecha_de_creacion_el_orden_sigue_siendo_estable()
    {
        await Prenda("p1", "Vestido rosa");
        // Sin avanzar el reloj: los 25 pedidos nacen en el mismo instante.
        for (var i = 0; i < 25; i++) await _svc.CrearAsync("c1", Pedido("v-p1"));

        var primera = new List<string>();
        var segunda = new List<string>();
        for (var p = 1; p <= 3; p++)
        {
            primera.AddRange((await _svc.BuscarAsync(null, null, null, p, 10)).Items.Select(a => a.Id));
            segunda.AddRange((await _svc.BuscarAsync(null, null, null, p, 10)).Items.Select(a => a.Id));
        }

        Assert.Equal(25, primera.Distinct().Count());
        Assert.Equal(primera, segunda);
    }

    [Fact]
    public async Task Una_pagina_mas_alla_del_final_viene_vacia_con_el_total_real()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(5);

        var pag = await _svc.BuscarAsync(null, null, null, 9, 10);

        Assert.Empty(pag.Items);
        Assert.Equal(5, pag.Total);
        Assert.False(pag.HayMas);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task Una_pagina_menor_a_uno_se_toma_como_la_primera(int pedida, int esperada)
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(3);
        Assert.Equal(esperada, (await _svc.BuscarAsync(null, null, null, pedida, 10)).Pagina);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(500, 50)]
    public async Task El_tamano_de_pagina_se_acota_entre_1_y_50(int pedido, int esperado)
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(2);
        Assert.Equal(esperado, (await _svc.BuscarAsync(null, null, null, 1, pedido)).Tamano);
    }

    [Fact]
    public async Task Sin_pedidos_la_pagina_viene_vacia()
    {
        var pag = await _svc.BuscarAsync(null, null, null, 1, 20);
        Assert.Empty(pag.Items);
        Assert.Equal(0, pag.Total);
        Assert.False(pag.HayMas);
    }

    // ── Filtros: el total es el de lo filtrado ──

    [Fact]
    public async Task El_total_cuenta_solo_los_pedidos_que_cumplen_el_filtro()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(7);
        await _svc.CrearAsync("c1", Pedido("v-p1", "sin_anticipo"));
        await _svc.CrearAsync("c1", Pedido("v-p1", "anticipo"));

        var compras = await _svc.BuscarAsync(null, null, "compras", 1, 5);
        var apartados = await _svc.BuscarAsync(null, null, "apartados", 1, 5);

        Assert.Equal(7, compras.Total);
        Assert.Equal(5, compras.Items.Count);
        Assert.True(compras.HayMas);
        Assert.Equal(2, apartados.Total);
        Assert.All(apartados.Items, a => Assert.NotEqual("compra", a.Modalidad));
    }

    [Fact]
    public async Task Filtra_por_estado_y_un_estado_inexistente_no_devuelve_nada()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(3);
        var activo = await _svc.CrearAsync("c1", Pedido("v-p1", "sin_anticipo"));
        await _svc.CancelarAsync(activo.Id);

        Assert.Equal(1, (await _svc.BuscarAsync("cancelado", null, null, 1, 20)).Total);
        Assert.Equal(3, (await _svc.BuscarAsync("liquidado", null, null, 1, 20)).Total);
        Assert.Equal(0, (await _svc.BuscarAsync("inventado", null, null, 1, 20)).Total);
    }

    [Fact]
    public async Task Por_entregar_y_entregado_separan_las_compras_segun_su_entrega()
    {
        await Prenda("p1", "Vestido rosa");
        var compras = await Compras(4);
        foreach (var siguiente in new[] { "listo", "entregado" })
            await _svc.AvanzarEntregaAsync(compras[0].Id, siguiente);

        var porEntregar = await _svc.BuscarAsync("por_entregar", null, "compras", 1, 20);
        var entregadas = await _svc.BuscarAsync("entregado", null, "compras", 1, 20);

        Assert.Equal(3, porEntregar.Total);
        Assert.Equal(1, entregadas.Total);
        Assert.Equal(compras[0].Id, Assert.Single(entregadas.Items).Id);
    }

    [Fact]
    public async Task Por_vencer_incluye_a_quien_le_quedan_3_dias_o_menos_y_los_ordena_por_vencimiento()
    {
        await Prenda("p1", "Vestido rosa");
        var vence7 = await _svc.CrearAsync("c1", Pedido("v-p1", "anticipo"));        // 7 días
        _reloj.Ahora = _reloj.Ahora.AddMinutes(1);
        var vence2 = await _svc.CrearAsync("c1", Pedido("v-p1", "sin_anticipo"));    // 2 días

        var hoy = await _svc.BuscarAsync("por_vencer", null, null, 1, 20);
        Assert.Equal(vence2.Id, Assert.Single(hoy.Items).Id);

        // 3 días después al de 7 le quedan 4: todavía no. Al día siguiente le quedan 3: ya sí.
        _reloj.Ahora = _reloj.Ahora.AddDays(3);
        Assert.DoesNotContain((await _svc.BuscarAsync("por_vencer", null, null, 1, 20)).Items, a => a.Id == vence7.Id);
        _reloj.Ahora = _reloj.Ahora.AddDays(1);
        var manana = await _svc.BuscarAsync("por_vencer", null, null, 1, 20);
        Assert.Contains(manana.Items, a => a.Id == vence7.Id);
    }

    [Fact]
    public async Task Por_vencer_va_del_que_vence_primero_al_que_vence_despues()
    {
        await Prenda("p1", "Vestido rosa");
        var tarde = await _svc.CrearAsync("c1", Pedido("v-p1", "sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddHours(5);
        var tardisimo = await _svc.CrearAsync("c1", Pedido("v-p1", "sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddHours(-3);

        var lista = (await _svc.BuscarAsync("por_vencer", null, null, 1, 20)).Items.Select(a => a.Id).ToList();

        Assert.Equal(new[] { tarde.Id, tardisimo.Id }, lista);
    }

    // ── Búsqueda ──

    [Fact]
    public async Task Busca_por_folio_clienta_telefono_y_prenda_sin_distinguir_mayusculas()
    {
        await Prenda("p1", "Vestido Rosa");
        await Prenda("p2", "Blusa Azul");
        var deAna = await _svc.CrearAsync("c1", Pedido("v-p1"));
        var deLuz = await _svc.CrearAsync("c2", Pedido("v-p2", clienta: "c2"));

        Assert.Equal(deAna.Id, Assert.Single((await _svc.BuscarAsync(null, deAna.Folio.ToLowerInvariant(), null, 1, 20)).Items).Id);
        Assert.Equal(deLuz.Id, Assert.Single((await _svc.BuscarAsync(null, "LUZ", null, 1, 20)).Items).Id);
        Assert.Equal(deLuz.Id, Assert.Single((await _svc.BuscarAsync(null, "99988", null, 1, 20)).Items).Id);
        Assert.Equal(deAna.Id, Assert.Single((await _svc.BuscarAsync(null, "vestido", null, 1, 20)).Items).Id);
    }

    [Fact]
    public async Task La_busqueda_pagina_su_propio_total()
    {
        await Prenda("p1", "Vestido rosa");
        await Prenda("p2", "Blusa azul");
        await Compras(12, "v-p1");
        await Compras(3, "v-p2");

        var pag = await _svc.BuscarAsync(null, "vestido", null, 1, 5);

        Assert.Equal(12, pag.Total);
        Assert.Equal(5, pag.Items.Count);
        Assert.True(pag.HayMas);
    }

    [Fact]
    public async Task Una_busqueda_sin_coincidencias_o_con_espacios_no_falla()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(2);

        Assert.Equal(0, (await _svc.BuscarAsync(null, "zzz", null, 1, 20)).Total);
        Assert.Equal(2, (await _svc.BuscarAsync(null, "   ", null, 1, 20)).Total);
    }

    [Fact]
    public async Task Un_texto_larguisimo_se_recorta_en_vez_de_fallar()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(1);
        var pag = await _svc.BuscarAsync(null, new string('a', 5000), null, 1, 20);
        Assert.Equal(0, pag.Total);
    }

    [Fact]
    public async Task Los_comodines_de_busqueda_se_tratan_como_texto_normal()
    {
        await Prenda("p1", "Vestido rosa");
        await Compras(2);
        Assert.Equal(0, (await _svc.BuscarAsync(null, "%", null, 1, 20)).Total);
        Assert.Equal(0, (await _svc.BuscarAsync(null, "_", null, 1, 20)).Total);
    }

    // ── Conteos de las pestañas ──

    [Fact]
    public async Task Los_conteos_dan_apartados_activos_y_compras_por_entregar()
    {
        await Prenda("p1", "Vestido rosa");
        await _svc.CrearAsync("c1", Pedido("v-p1", "sin_anticipo"));
        await _svc.CrearAsync("c1", Pedido("v-p1", "anticipo"));
        var compras = await Compras(3);
        await _svc.AvanzarEntregaAsync(compras[0].Id, "listo");
        await _svc.AvanzarEntregaAsync(compras[0].Id, "entregado");

        var c = await _svc.ConteosAsync();

        Assert.Equal(2, c.ApartadosActivos);
        Assert.Equal(2, c.ComprasPorEntregar);
    }

    [Fact]
    public async Task Los_conteos_sin_pedidos_son_cero()
    {
        var c = await _svc.ConteosAsync();
        Assert.Equal(0, c.ApartadosActivos);
        Assert.Equal(0, c.ComprasPorEntregar);
    }

    // ── La lista completa de siempre no se corta ──

    [Fact]
    public async Task La_lista_completa_de_siempre_sigue_trayendo_todo_sin_tope()
    {
        await Prenda("p1", "Vestido rosa", stock: 500);
        await Compras(60);

        var todos = await _svc.ListarAsync(null, null, null);

        Assert.Equal(60, todos.Count);
    }

    [Fact]
    public async Task Las_paginas_traen_la_foto_de_la_prenda_igual_que_la_lista()
    {
        await Prenda("p1", "Vestido rosa");
        (await _productos.ObtenerPorIdAsync("p1"))!.FijarImagenes(new[] { ("https://r2/p1/x/800.webp", (string?)"col-p1") });
        await Compras(3);

        var pag = await _svc.BuscarAsync(null, null, null, 1, 2);

        Assert.All(pag.Items, a => Assert.Equal("https://r2/p1/x/800.webp", a.Lineas[0].ImagenUrl));
    }
}
