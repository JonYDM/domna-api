using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Casos de uso de apartados/ventas con repositorios en memoria y reloj fijo.</summary>
public class ApartadoServiceTests
{
    // Jueves 1 de enero de 2026, 12:00 UTC (06:00 en México).
    private readonly RelojFijo _reloj = new();
    private readonly ProductoRepoFake _productos = new();
    private readonly ApartadoRepoFake _apartados = new();
    private readonly ConfigRepoFake _config = new();
    private readonly ClientaRepoFake _clientas;
    private readonly Clienta _ana;
    private readonly ApartadoService _svc;

    public ApartadoServiceTests()
    {
        _ana = Clienta.Crear("c1", "Ana", "Lopez", "7771234567", "ana@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        _clientas = new ClientaRepoFake(_ana);
        _svc = new ApartadoService(_apartados, _productos, _clientas, _config, _reloj);
    }

    // ── Fixtures ──

    /// <summary>Vestido con una sola variante "v-{id}" (talla M, un color), con stock por sucursal.</summary>
    private async Task<Producto> Vestido(string id = "p1", int temixco = 5, int azteca = 2, bool permiteApartado = true)
    {
        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { temixco } },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = new[] { azteca } },
        };
        var p = Producto.Crear(
            id, $"Vestido {id}", null, "vestidos", Silueta.Vestido, 600m, 250m, permiteApartado,
            new[] { new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa) },
            new[] { "M" }, stock, _reloj.Ahora,
            nuevoIdColor: () => $"col-{id}", nuevoIdVariante: () => $"v-{id}");
        await _productos.AgregarAsync(p);
        return p;
    }

    private static CrearApartadoInput Pedido(
        string modalidad = "sin_anticipo", string entrega = "temixco", int cantidad = 1,
        string varianteId = "v-p1", string? clientaId = "c1", string? canal = "online") =>
        new(new List<LineaCarritoInput> { new(varianteId, cantidad) }, modalidad, entrega, canal, clientaId, "efectivo");

    private static int Stock(Producto p, SucursalId s) => p.Variantes[0].StockDe(s);

    private async Task<int> Disponible(string varianteId, SucursalId s)
    {
        var prod = (await _productos.ListarAsync(true)).First(p => p.Variantes.Any(v => v.Id == varianteId));
        var reservado = await _apartados.ObtenerReservadoAsync(varianteId);
        return prod.Variantes.First(v => v.Id == varianteId).StockDe(s)
            - reservado.GetValueOrDefault(s == SucursalId.Temixco ? "temixco" : "azteca");
    }

    // ── Cotizar ──

    [Fact]
    public async Task Cotizar_con_anticipo_calcula_30_por_ciento_redondeado_hacia_arriba()
    {
        await Vestido();
        var cot = await _svc.CotizarAsync("c1", Pedido("anticipo"));

        Assert.Equal(600m, cot.Subtotal);
        Assert.Equal(600m, cot.Total);
        Assert.Equal(180m, cot.Anticipo);
        Assert.Equal(_reloj.Ahora.AddDays(7), cot.VenceEl);
        Assert.False(cot.RequiereTraslado);
        Assert.Equal("temixco", cot.SucursalOrigen);
    }

    [Fact]
    public async Task Cotizar_anticipo_redondea_hacia_arriba_con_centavos()
    {
        var p = await Vestido();
        p.CambiarPrecio(599m); // 30% = 179.70 → 180
        var cot = await _svc.CotizarAsync("c1", Pedido("anticipo"));
        Assert.Equal(180m, cot.Anticipo);
    }

    [Fact]
    public async Task Cotizar_sin_anticipo_no_pide_anticipo_y_vence_en_2_dias()
    {
        await Vestido();
        var cot = await _svc.CotizarAsync("c1", Pedido("sin_anticipo"));
        Assert.Equal(0m, cot.Anticipo);
        Assert.Equal(_reloj.Ahora.AddDays(2), cot.VenceEl);
    }

    [Fact]
    public async Task Cotizar_compra_cobra_el_total_completo()
    {
        await Vestido();
        var cot = await _svc.CotizarAsync("c1", Pedido("compra"));
        Assert.Equal(600m, cot.Anticipo);
        Assert.Equal(_reloj.Ahora, cot.VenceEl);
    }

    [Fact]
    public async Task Cotizar_suma_la_penalizacion_pendiente_de_la_clienta()
    {
        await Vestido();
        _ana.AjustarPenalizacion(100m);
        var cot = await _svc.CotizarAsync("c1", Pedido("anticipo"));

        Assert.Equal(600m, cot.Subtotal);
        Assert.Equal(100m, cot.Penalizacion);
        Assert.Equal(700m, cot.Total);
        Assert.Equal(210m, cot.Anticipo); // 30% del total con penalización
    }

    [Fact]
    public async Task Cotizar_no_pide_la_sucursal_de_entrega_si_la_otra_es_la_unica_con_stock()
    {
        await Vestido(temixco: 0, azteca: 3);
        var cot = await _svc.CotizarAsync("c1", Pedido(entrega: "temixco"));

        Assert.Equal("azteca", cot.SucursalOrigen);
        Assert.True(cot.RequiereTraslado);
    }

    [Fact]
    public async Task Cotizar_con_traslado_estima_dos_dias_habiles_saltando_domingo()
    {
        await Vestido(temixco: 0, azteca: 3);
        _reloj.Ahora = new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc); // sábado
        var cot = await _svc.CotizarAsync("c1", Pedido(entrega: "temixco"));

        // domingo no cuenta: lunes (1) y martes (2)
        Assert.Equal(new DateTime(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc), cot.ListoEstimado);
    }

    [Fact]
    public async Task Cotizar_prefiere_la_sucursal_de_entrega_si_cubre_todo_el_pedido()
    {
        await Vestido(temixco: 5, azteca: 5);
        var cot = await _svc.CotizarAsync("c1", Pedido(entrega: "azteca", cantidad: 2));
        Assert.Equal("azteca", cot.SucursalOrigen);
        Assert.False(cot.RequiereTraslado);
    }

    [Fact]
    public async Task Cotizar_usa_la_otra_sucursal_si_la_de_entrega_no_alcanza_para_el_pedido_completo()
    {
        await Vestido(temixco: 5, azteca: 2);
        var cot = await _svc.CotizarAsync("c1", Pedido(entrega: "azteca", cantidad: 3));
        Assert.Equal("temixco", cot.SucursalOrigen);
        Assert.True(cot.RequiereTraslado);
    }

    [Fact]
    public async Task Cotizar_sin_stock_no_lanza_y_devuelve_sucursal_origen_nula()
    {
        await Vestido(temixco: 1, azteca: 1);
        var cot = await _svc.CotizarAsync("c1", Pedido(cantidad: 5));
        Assert.Null(cot.SucursalOrigen);
        Assert.Equal(0m, cot.Subtotal);
    }

    [Fact]
    public async Task Cotizar_variante_inexistente_responde_404()
    {
        await Vestido();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CotizarAsync("c1", Pedido(varianteId: "no-existe")));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Cotizar_producto_desactivado_responde_404()
    {
        var p = await Vestido();
        p.Desactivar();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CotizarAsync("c1", Pedido()));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── Crear ──

    [Fact]
    public async Task Crear_apartado_con_anticipo_queda_activo_con_su_primer_abono()
    {
        var p = await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));

        Assert.Equal("activo", ap.Estado);
        Assert.Equal("anticipo", ap.Modalidad);
        Assert.Equal("DOM-0001", ap.Folio);
        Assert.Equal("Ana Lopez", ap.ClientaNombre);
        Assert.Equal(180m, ap.Pagado);
        Assert.Equal(420m, ap.Saldo);
        Assert.Equal("anticipo", Assert.Single(ap.Abonos).Concepto);
        // Apartar NO descuenta stock físico: solo lo reserva.
        Assert.Equal(5, Stock(p, SucursalId.Temixco));
    }

    [Fact]
    public async Task Los_folios_son_correlativos()
    {
        await Vestido();
        var a1 = await _svc.CrearAsync("c1", Pedido());
        var a2 = await _svc.CrearAsync("c1", Pedido());
        Assert.Equal("DOM-0001", a1.Folio);
        Assert.Equal("DOM-0002", a2.Folio);
    }

    [Fact]
    public async Task Apartar_reserva_stock_sin_tocar_el_fisico()
    {
        var p = await Vestido(temixco: 5, azteca: 2);
        await _svc.CrearAsync("c1", Pedido(cantidad: 3));

        Assert.Equal(5, Stock(p, SucursalId.Temixco));
        Assert.Equal(2, await Disponible("v-p1", SucursalId.Temixco));
    }

    [Fact]
    public async Task No_se_puede_apartar_lo_que_ya_esta_reservado_por_otra_clienta()
    {
        await Vestido(temixco: 1, azteca: 0);
        await _svc.CrearAsync("c1", Pedido());

        var otra = Clienta.Crear("c2", "Luisa", "Perez", "7779876543", "luisa@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        await _clientas.AgregarAsync(otra);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c2", Pedido(clientaId: "c2")));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Compra_se_liquida_al_instante_y_descuenta_stock_fisico()
    {
        var p = await Vestido(temixco: 5, azteca: 2);
        var ap = await _svc.CrearAsync("c1", Pedido("compra", cantidad: 2, canal: "fisico"));

        Assert.Equal("liquidado", ap.Estado);
        Assert.Equal("fisico", ap.Canal);
        Assert.Equal(0m, ap.Saldo);
        Assert.Equal("compra", Assert.Single(ap.Abonos).Concepto);
        Assert.Equal(3, Stock(p, SucursalId.Temixco));
        Assert.Equal(2, Stock(p, SucursalId.Azteca)); // la otra sucursal no se toca
    }

    [Fact]
    public async Task Compra_descuenta_de_la_sucursal_origen_aunque_la_entrega_sea_otra()
    {
        var p = await Vestido(temixco: 0, azteca: 3);
        var ap = await _svc.CrearAsync("c1", Pedido("compra", entrega: "temixco"));

        Assert.True(ap.RequiereTraslado);
        Assert.Equal(2, Stock(p, SucursalId.Azteca));
        Assert.Equal(0, Stock(p, SucursalId.Temixco));
    }

    [Fact]
    public async Task Compra_sin_clienta_es_publico_general()
    {
        await Vestido();
        var ap = await _svc.CrearAsync(null, Pedido("compra", clientaId: null, canal: "fisico"));
        Assert.Equal("c-publico", ap.ClientaId);
        Assert.Equal("Público general", ap.ClientaNombre);
    }

    [Fact]
    public async Task Compra_con_clienta_inexistente_responde_404()
    {
        await Vestido();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("fantasma", Pedido("compra", clientaId: "fantasma")));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task El_limite_de_apartados_activos_bloquea_el_cuarto()
    {
        await Vestido(temixco: 10, azteca: 10);
        for (var i = 0; i < 3; i++) await _svc.CrearAsync("c1", Pedido());

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", Pedido()));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("3 apartados activos", ex.Message);
    }

    [Fact]
    public async Task Las_compras_no_cuentan_para_el_limite_de_apartados()
    {
        await Vestido(temixco: 10, azteca: 10);
        for (var i = 0; i < 3; i++) await _svc.CrearAsync("c1", Pedido());

        var compra = await _svc.CrearAsync("c1", Pedido("compra"));
        Assert.Equal("liquidado", compra.Estado);
    }

    [Fact]
    public async Task Liquidar_o_cancelar_un_apartado_libera_un_lugar_del_limite()
    {
        await Vestido(temixco: 10, azteca: 10);
        var primero = await _svc.CrearAsync("c1", Pedido());
        await _svc.CrearAsync("c1", Pedido());
        await _svc.CrearAsync("c1", Pedido());

        await _svc.CancelarAsync(primero.Id);

        var cuarto = await _svc.CrearAsync("c1", Pedido());
        Assert.Equal("activo", cuarto.Estado);
    }

    [Fact]
    public async Task El_limite_es_por_clienta()
    {
        await Vestido(temixco: 10, azteca: 10);
        var otra = Clienta.Crear("c2", "Luisa", "Perez", "7779876543", "luisa@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        await _clientas.AgregarAsync(otra);
        for (var i = 0; i < 3; i++) await _svc.CrearAsync("c1", Pedido());

        var deLuisa = await _svc.CrearAsync("c2", Pedido(clientaId: "c2"));
        Assert.Equal("activo", deLuisa.Estado);
    }

    [Fact]
    public async Task Una_prenda_que_no_permite_apartado_solo_se_vende_de_contado()
    {
        var p = await Vestido(permiteApartado: false);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", Pedido("anticipo")));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("solo se vende de contado", ex.Message);

        var compra = await _svc.CrearAsync("c1", Pedido("compra"));
        Assert.Equal("liquidado", compra.Estado);
        Assert.Equal(4, Stock(p, SucursalId.Temixco));
    }

    [Fact]
    public async Task Una_prenda_no_apartable_en_el_carrito_bloquea_todo_el_apartado()
    {
        await Vestido("p1");
        await Vestido("p2", permiteApartado: false);
        var input = new CrearApartadoInput(
            new List<LineaCarritoInput> { new("v-p1", 1), new("v-p2", 1) },
            "anticipo", "temixco", "online", "c1", "efectivo");

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", input));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Tienda_suspendida_responde_423_y_no_crea_nada()
    {
        await Vestido();
        _config.Config.FijarSuspendida(true);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", Pedido()));
        Assert.Equal(423, ex.StatusCode);
        Assert.Empty(await _svc.ListarAsync(null, null, null));
    }

    [Fact]
    public async Task Carrito_vacio_o_cantidad_invalida_responde_400()
    {
        await Vestido();
        var vacio = new CrearApartadoInput(new List<LineaCarritoInput>(), "anticipo", "temixco", "online", "c1", "efectivo");
        var ex1 = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", vacio));
        Assert.Equal(400, ex1.StatusCode);

        var ex2 = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", Pedido(cantidad: 0)));
        Assert.Equal(400, ex2.StatusCode);
    }

    [Fact]
    public async Task La_misma_variante_en_dos_lineas_valida_el_stock_sumado()
    {
        await Vestido(temixco: 5, azteca: 0);
        var input = new CrearApartadoInput(
            new List<LineaCarritoInput> { new("v-p1", 3), new("v-p1", 3) },
            "anticipo", "temixco", "online", "c1", "efectivo");

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", input));
        Assert.Equal(409, ex.StatusCode); // 3 + 3 = 6 > 5
    }

    [Fact]
    public async Task Carrito_con_varios_productos_congela_precio_y_descuenta_cada_uno_al_comprar()
    {
        var p1 = await Vestido("p1", temixco: 5, azteca: 0);
        var p2 = await Vestido("p2", temixco: 4, azteca: 0);
        p2.CambiarPrecio(300m);
        var input = new CrearApartadoInput(
            new List<LineaCarritoInput> { new("v-p1", 2), new("v-p2", 1) },
            "compra", "temixco", "fisico", "c1", "efectivo");

        var ap = await _svc.CrearAsync("c1", input);

        Assert.Equal(1500m, ap.Total); // 600×2 + 300
        Assert.Equal(2, ap.Lineas.Count);
        Assert.Equal(3, Stock(p1, SucursalId.Temixco));
        Assert.Equal(3, Stock(p2, SucursalId.Temixco));
    }

    [Fact]
    public async Task El_precio_queda_congelado_aunque_el_producto_suba_despues()
    {
        var p = await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido());
        p.CambiarPrecio(900m);

        var releido = await _svc.ObtenerAsync(ap.Id);
        Assert.Equal(600m, releido.Total);
        Assert.Equal(600m, releido.Lineas[0].Precio);
    }

    [Fact]
    public async Task La_penalizacion_pendiente_se_cobra_en_el_siguiente_apartado_y_se_limpia()
    {
        await Vestido();
        _ana.AjustarPenalizacion(100m);

        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));

        Assert.Equal(100m, ap.Penalizacion);
        Assert.Equal(700m, ap.Total);
        Assert.Equal(210m, ap.Pagado);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Un_apartado_fallido_no_limpia_la_penalizacion()
    {
        await Vestido(temixco: 0, azteca: 0);
        _ana.AjustarPenalizacion(100m);

        await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c1", Pedido()));
        Assert.Equal(100m, _ana.PenalizacionPendiente);
    }

    // ── Abonar ──

    [Fact]
    public async Task Abono_parcial_no_liquida_ni_descuenta_stock_y_extiende_la_vigencia()
    {
        var p = await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(1);

        var tras = await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 100m, "tarjeta"));

        Assert.Equal("activo", tras.Estado);
        Assert.Equal("anticipo", tras.Modalidad);
        Assert.Equal(500m, tras.Saldo);
        Assert.Equal(_reloj.Ahora.AddDays(7), tras.VenceEl);
        Assert.Equal(5, Stock(p, SucursalId.Temixco));
    }

    [Fact]
    public async Task Abonar_el_saldo_exacto_liquida_y_descuenta_el_stock_fisico()
    {
        var p = await Vestido(temixco: 5, azteca: 2);
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo", cantidad: 2));
        Assert.Equal(5, Stock(p, SucursalId.Temixco));

        var tras = await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, ap.Saldo, "efectivo"));

        Assert.Equal("liquidado", tras.Estado);
        Assert.Equal(0m, tras.Saldo);
        Assert.Equal("liquidacion", tras.Abonos.Last().Concepto);
        Assert.Equal(3, Stock(p, SucursalId.Temixco));
        // Ya no hay reserva (ahora es venta): el disponible coincide con el físico.
        Assert.Equal(3, await Disponible("v-p1", SucursalId.Temixco));
    }

    [Fact]
    public async Task Abonar_en_dos_pagos_liquida_con_el_segundo_y_descuenta_una_sola_vez()
    {
        var p = await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));

        await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 250m, "efectivo"));
        Assert.Equal(5, Stock(p, SucursalId.Temixco));

        var tras = await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 350m, "efectivo"));
        Assert.Equal("liquidado", tras.Estado);
        Assert.Equal(4, Stock(p, SucursalId.Temixco));
    }

    [Fact]
    public async Task Abonar_mas_que_el_saldo_o_cero_responde_400()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));

        var ex1 = await Assert.ThrowsAsync<DomnaException>(() => _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 601m, "efectivo")));
        Assert.Equal(400, ex1.StatusCode);
        var ex2 = await Assert.ThrowsAsync<DomnaException>(() => _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 0m, "efectivo")));
        Assert.Equal(400, ex2.StatusCode);
    }

    [Fact]
    public async Task Abonar_a_un_apartado_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.AbonarAsync(new RegistrarAbonoInput("nada", 10m, "efectivo")));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Abonar_a_un_apartado_ya_liquidado_no_vuelve_a_descontar_stock()
    {
        var p = await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));
        Assert.Equal(4, Stock(p, SucursalId.Temixco));

        await Assert.ThrowsAsync<DomnaException>(() => _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 1m, "efectivo")));
        Assert.Equal(4, Stock(p, SucursalId.Temixco));
    }

    // ── Cancelar ──

    [Fact]
    public async Task Cancelar_libera_la_reserva_para_que_otra_clienta_pueda_apartar()
    {
        await Vestido(temixco: 1, azteca: 0);
        var otra = Clienta.Crear("c2", "Luisa", "Perez", "7779876543", "luisa@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        await _clientas.AgregarAsync(otra);

        var ap = await _svc.CrearAsync("c1", Pedido());
        Assert.Equal(0, await Disponible("v-p1", SucursalId.Temixco));
        await Assert.ThrowsAsync<DomnaException>(() => _svc.CrearAsync("c2", Pedido(clientaId: "c2")));

        var cancelado = await _svc.CancelarAsync(ap.Id);
        Assert.Equal("cancelado", cancelado.Estado);
        Assert.Equal(1, await Disponible("v-p1", SucursalId.Temixco));

        var deLuisa = await _svc.CrearAsync("c2", Pedido(clientaId: "c2"));
        Assert.Equal("activo", deLuisa.Estado);
    }

    [Fact]
    public async Task Cancelar_no_toca_el_stock_fisico()
    {
        var p = await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo", cantidad: 2));
        await _svc.CancelarAsync(ap.Id);
        Assert.Equal(5, Stock(p, SucursalId.Temixco));
    }

    [Fact]
    public async Task Cancelar_conserva_el_anticipo_registrado_y_no_genera_penalizacion()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));
        var cancelado = await _svc.CancelarAsync(ap.Id);

        Assert.Equal(180m, cancelado.Pagado);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Cancelar_un_liquidado_o_un_cancelado_responde_400()
    {
        await Vestido(temixco: 10);
        var compra = await _svc.CrearAsync("c1", Pedido("compra"));
        var ex1 = await Assert.ThrowsAsync<DomnaException>(() => _svc.CancelarAsync(compra.Id));
        Assert.Equal(400, ex1.StatusCode);

        var ap = await _svc.CrearAsync("c1", Pedido());
        await _svc.CancelarAsync(ap.Id);
        await Assert.ThrowsAsync<DomnaException>(() => _svc.CancelarAsync(ap.Id));
    }

    // ── Vencimientos ──

    [Fact]
    public async Task Un_apartado_sin_anticipo_vence_a_los_2_dias_y_penaliza_una_sola_vez()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));

        _reloj.Ahora = _reloj.Ahora.AddDays(3);
        var visto = await _svc.ObtenerAsync(ap.Id);
        Assert.Equal("vencido", visto.Estado);
        Assert.Equal(100m, _ana.PenalizacionPendiente);

        // Leer muchas veces no vuelve a penalizar.
        await _svc.ObtenerAsync(ap.Id);
        await _svc.ListarAsync(null, null, null);
        await _svc.ListarDeClientaAsync("c1");
        Assert.Equal(100m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Con_anticipo_el_apartado_aguanta_7_dias_y_vence_al_octavo()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));

        _reloj.Ahora = _reloj.Ahora.AddDays(6);
        Assert.Equal("activo", (await _svc.ObtenerAsync(ap.Id)).Estado);

        _reloj.Ahora = _reloj.Ahora.AddDays(1); // día 7: ya es la fecha de vencimiento
        Assert.Equal("vencido", (await _svc.ObtenerAsync(ap.Id)).Estado);
    }

    [Fact]
    public async Task Al_vencer_se_libera_la_reserva()
    {
        await Vestido(temixco: 1, azteca: 0);
        await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        Assert.Equal(0, await Disponible("v-p1", SucursalId.Temixco));

        _reloj.Ahora = _reloj.Ahora.AddDays(3);
        // Cotizar (sin leer apartados antes) ya debe ver la prenda libre.
        var cot = await _svc.CotizarAsync("c1", Pedido());
        Assert.Equal("temixco", cot.SucursalOrigen);
    }

    [Fact]
    public async Task Crear_despues_de_vencer_cobra_la_penalizacion_aunque_no_se_haya_leido_antes()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var nuevo = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));

        Assert.Equal(100m, nuevo.Penalizacion);
        Assert.Equal(700m, nuevo.Total);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Un_apartado_vencido_no_cuenta_para_el_limite_aunque_nadie_lo_haya_leido()
    {
        await Vestido(temixco: 10, azteca: 10);
        for (var i = 0; i < 3; i++) await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var nuevo = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        Assert.Equal("activo", nuevo.Estado);
    }

    [Fact]
    public async Task Vencimientos_multiples_suman_una_penalizacion_cada_uno()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        await _svc.CrearAsync("c1", Pedido("sin_anticipo"));

        _reloj.Ahora = _reloj.Ahora.AddDays(3);
        await _svc.ListarAsync(null, null, null);

        Assert.Equal(200m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Abonar_a_un_apartado_vencido_responde_400()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 100m, "efectivo")));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Un_abono_parcial_a_tiempo_evita_el_vencimiento_original()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(1);
        await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 100m, "efectivo"));

        _reloj.Ahora = _reloj.Ahora.AddDays(3); // pasó la fecha original pero no la nueva
        Assert.Equal("activo", (await _svc.ObtenerAsync(ap.Id)).Estado);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Un_apartado_de_publico_general_que_vence_no_penaliza_a_nadie()
    {
        await Vestido();
        await _svc.CrearAsync(null, Pedido("sin_anticipo", clientaId: null, canal: "fisico"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var lista = await _svc.ListarAsync("vencido", null, null);
        Assert.Single(lista);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task Un_liquidado_nunca_vence()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 600m, "efectivo"));

        _reloj.Ahora = _reloj.Ahora.AddDays(60);
        Assert.Equal("liquidado", (await _svc.ObtenerAsync(ap.Id)).Estado);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
    }

    // ── Entrega ──

    [Fact]
    public async Task Entregar_exige_estar_liquidado()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.AvanzarEntregaAsync(ap.Id, "entregado"));
        Assert.Equal(400, ex.StatusCode);

        await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, ap.Saldo, "efectivo"));
        var tras = await _svc.AvanzarEntregaAsync(ap.Id, "entregado");
        Assert.Equal("entregado", tras.EstadoEntrega);
    }

    [Fact]
    public async Task Avanzar_entrega_con_valor_invalido_responde_400()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido());
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.AvanzarEntregaAsync(ap.Id, "volando"));
        Assert.Equal(400, ex.StatusCode);
    }

    // ── Listado y filtros ──

    [Fact]
    public async Task Listar_separa_apartados_de_compras()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("anticipo"));
        await _svc.CrearAsync("c1", Pedido("compra"));

        Assert.Single(await _svc.ListarAsync(null, null, "apartados"));
        Assert.Single(await _svc.ListarAsync(null, null, "compras"));
        Assert.Equal(2, (await _svc.ListarAsync(null, null, null)).Count);
    }

    [Fact]
    public async Task Listar_filtra_por_estado_y_por_texto()
    {
        await Vestido(temixco: 10, azteca: 10);
        var uno = await _svc.CrearAsync("c1", Pedido("anticipo"));
        await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.CancelarAsync(uno.Id);

        Assert.Single(await _svc.ListarAsync("cancelado", null, null));
        Assert.Single(await _svc.ListarAsync("liquidado", null, null));
        Assert.Empty(await _svc.ListarAsync("activo", null, null));
        Assert.Equal(2, (await _svc.ListarAsync(null, "ana", null)).Count);
        Assert.Single(await _svc.ListarAsync(null, "DOM-0002", null));
        Assert.Empty(await _svc.ListarAsync(null, "zzz", null));
    }

    [Fact]
    public async Task Por_entregar_son_los_liquidados_pendientes_de_entrega()
    {
        await Vestido(temixco: 10, azteca: 10);
        var compra = await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.CrearAsync("c1", Pedido("anticipo"));

        Assert.Single(await _svc.ListarAsync("por_entregar", null, null));

        await _svc.AvanzarEntregaAsync(compra.Id, "entregado");
        Assert.Empty(await _svc.ListarAsync("por_entregar", null, null));
        Assert.Single(await _svc.ListarAsync("entregado", null, null));
    }

    [Fact]
    public async Task Por_vencer_usa_el_reloj_del_sistema_y_ordena_por_fecha()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("anticipo"));   // vence en 7 días
        await _svc.CrearAsync("c1", Pedido("sin_anticipo")); // vence en 2 días

        var lista = await _svc.ListarAsync("por_vencer", null, null);
        Assert.Single(lista);
        Assert.Equal("sin_anticipo", lista[0].Modalidad);
    }

    [Fact]
    public async Task ListarDeClienta_solo_trae_las_de_esa_clienta()
    {
        await Vestido(temixco: 10, azteca: 10);
        var otra = Clienta.Crear("c2", "Luisa", "Perez", "7779876543", "luisa@correo.com", OrigenClienta.Registro, _reloj.Ahora);
        await _clientas.AgregarAsync(otra);
        await _svc.CrearAsync("c1", Pedido());
        await _svc.CrearAsync("c2", Pedido(clientaId: "c2"));

        var deAna = await _svc.ListarDeClientaAsync("c1");
        Assert.Single(deAna);
        Assert.Equal("c1", deAna[0].ClientaId);
    }

    [Fact]
    public async Task Obtener_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.ObtenerAsync("nada"));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── Corte del día y métricas ──

    [Fact]
    public async Task Corte_del_dia_cuenta_solo_lo_liquidado_hoy_en_esa_sucursal()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("compra", cantidad: 2)); // liquidada hoy en temixco
        await _svc.CrearAsync("c1", Pedido("anticipo"));            // activa: no cuenta

        var temixco = await _svc.CorteDiaAsync("temixco");
        Assert.Equal(1200m, temixco.VentasDia);
        Assert.Equal(2 * (600m - 250m), temixco.GananciaDia);
        Assert.Equal(2, temixco.PiezasDia);
        Assert.Equal(1, temixco.Tickets);

        var azteca = await _svc.CorteDiaAsync("azteca");
        Assert.Equal(0m, azteca.VentasDia);
        Assert.Equal(0, azteca.Tickets);
    }

    [Fact]
    public async Task Corte_del_dia_no_incluye_ventas_de_ayer()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("compra"));
        _reloj.Ahora = _reloj.Ahora.AddDays(1);

        var corte = await _svc.CorteDiaAsync("temixco");
        Assert.Equal(0m, corte.VentasDia);
    }

    [Fact]
    public async Task Metricas_reflejan_activos_saldo_y_ventas_del_mes()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("anticipo"));
        await _svc.CrearAsync("c1", Pedido("compra"));

        var m = await _svc.MetricasAsync(null);
        Assert.Equal(1, m.ApartadosActivos);
        Assert.Equal(600m, m.MontoActivos);
        Assert.Equal(420m, m.SaldoPorCobrar);
        Assert.Equal(600m, m.VentasMes);
        Assert.Equal(1, m.ComprasMes);
        Assert.Equal(1, m.PorEntregar);
    }

    [Fact]
    public async Task Metricas_conversion_ignora_las_compras_y_cuenta_vencidos_como_no_concretados()
    {
        await Vestido(temixco: 10, azteca: 10);
        var liquida = await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        await _svc.AbonarAsync(new RegistrarAbonoInput(liquida.Id, 600m, "efectivo"));
        await _svc.CrearAsync("c1", Pedido("sin_anticipo")); // este se vence
        await _svc.CrearAsync("c1", Pedido("compra"));       // no cuenta en la conversión
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var m = await _svc.MetricasAsync(null);
        Assert.Equal(50, m.Conversion);
        Assert.Equal(100m, m.PenalizacionesMes);
    }

    [Fact]
    public async Task Metricas_marcan_stock_bajo_con_el_disponible_real_descontando_reservas()
    {
        await Vestido(temixco: 3, azteca: 0);
        await _svc.CrearAsync("c1", Pedido(cantidad: 2)); // disponible = 3 - 2 = 1 → bajo

        var m = await _svc.MetricasAsync(null);
        var bajo = Assert.Single(m.StockBajo);
        Assert.Equal(1, bajo.Disponible);
    }
}
