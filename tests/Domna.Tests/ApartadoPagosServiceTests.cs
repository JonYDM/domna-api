using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Flujo completo del pedido en línea: nace por pagar y la caja de su sucursal lo confirma.</summary>
public class ApartadoPagosServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly ProductoRepoFake _productos = new();
    private readonly ApartadoRepoFake _apartados = new();
    private readonly Clienta _ana;
    private readonly ApartadoService _svc;
    private Producto _vestido = default!;

    private const string Duena = "u-duena";
    private const string CajeraTemixco = "u-caja-t";
    private const string CajeraAzteca = "u-caja-a";

    public ApartadoPagosServiceTests()
    {
        var ahora = _reloj.Ahora;
        _ana = Clienta.Crear("c1", "Ana", "Lopez", "7771234567", "ana@correo.com", OrigenClienta.Registro, ahora);
        var clientas = new ClientaRepoFake(
            _ana,
            Clienta.CrearStaff(Duena, "Jessica", "Jesly", "7774602303", "j@x.mx", Rol.Duena, "h", ahora),
            Clienta.CrearStaff(CajeraTemixco, "Caja", "Temixco", "7771004050", "ct@x.mx", Rol.Cajera, "h", ahora, SucursalId.Temixco),
            Clienta.CrearStaff(CajeraAzteca, "Caja", "Azteca", "7771004051", "ca@x.mx", Rol.Cajera, "h", ahora, SucursalId.Azteca));
        _svc = new ApartadoService(_apartados, _productos, clientas, new ConfigRepoFake(), _reloj);
    }

    private async Task Vestido(int temixco = 5, int azteca = 5)
    {
        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { temixco } },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = new[] { azteca } },
        };
        _vestido = Producto.Crear(
            "p1", "Vestido p1", null, "vestidos", Silueta.Vestido, 600m, 250m, true,
            new[] { new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa) },
            new[] { "M" }, stock, _reloj.Ahora, nuevoIdColor: () => "col", nuevoIdVariante: () => "v-p1");
        await _productos.AgregarAsync(_vestido);
    }

    private static CrearApartadoInput Pedido(string modalidad, string entrega = "temixco", int cantidad = 1, string metodo = "efectivo") =>
        new(new List<LineaCarritoInput> { new("v-p1", cantidad) }, modalidad, entrega, "online", "c1", metodo);

    private int Stock(SucursalId s) => _vestido.Variantes[0].StockDe(s);

    // ── Nacimiento del pedido en línea ──

    [Fact]
    public async Task Compra_en_linea_nace_por_pagar_reserva_stock_y_no_lo_descuenta()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));

        Assert.Equal("activo", ap.Estado);
        Assert.Equal(0m, ap.Pagado);
        Assert.Equal(600m, ap.PorConfirmar);
        Assert.Equal(600m, ap.Saldo);
        Assert.Equal("pendiente", Assert.Single(ap.Abonos).Estado);
        Assert.Equal(5, Stock(SucursalId.Temixco)); // sigue en el estante
        var reservado = await _apartados.ObtenerReservadoAsync("v-p1");
        Assert.Equal(1, reservado["temixco"]);       // pero nadie más puede apartarla
    }

    [Theory]
    [InlineData("efectivo")]
    [InlineData("tarjeta")]
    [InlineData("transferencia")]
    public async Task Ningun_metodo_de_pago_en_linea_cuenta_como_pagado_al_crear(string metodo)
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra", metodo: metodo));
        Assert.Equal(0m, ap.Pagado);
        Assert.Equal(metodo, ap.Abonos[0].Metodo);
    }

    [Fact]
    public async Task Anticipo_en_linea_tampoco_cuenta_como_pagado()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));
        Assert.Equal(0m, ap.Pagado);
        Assert.Equal(180m, ap.PorConfirmar);
        Assert.Equal(600m, ap.Saldo);
    }

    [Fact]
    public async Task Pedido_en_linea_da_dos_dias_para_pagar()
    {
        await Vestido();
        var compra = await _svc.CrearAsync("c1", Pedido("compra"));
        var anticipo = await _svc.CrearAsync("c1", Pedido("anticipo"));
        Assert.Equal(_reloj.Ahora.AddDays(2), compra.VenceEl);
        Assert.Equal(_reloj.Ahora.AddDays(2), anticipo.VenceEl);

        var cot = await _svc.CotizarAsync("c1", Pedido("compra"));
        Assert.Equal(_reloj.Ahora.AddDays(2), cot.VenceEl);
    }

    [Fact]
    public async Task Las_compras_en_linea_por_pagar_no_cuentan_para_el_limite_de_apartados()
    {
        await Vestido(temixco: 10, azteca: 10);
        for (var i = 0; i < 3; i++) await _svc.CrearAsync("c1", Pedido("sin_anticipo"));

        var compra = await _svc.CrearAsync("c1", Pedido("compra"));
        Assert.Equal("activo", compra.Estado);
    }

    [Fact]
    public async Task Las_compras_en_linea_por_pagar_no_inflan_las_metricas_de_apartados()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.CrearAsync("c1", Pedido("anticipo"));

        var m = await _svc.MetricasAsync(null);
        Assert.Equal(1, m.ApartadosActivos);
        Assert.Equal(0m, m.VentasMes);
    }

    // ── Confirmar ──

    [Fact]
    public async Task La_cajera_de_la_sucursal_confirma_liquida_y_se_descuenta_el_stock()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra", entrega: "temixco"));

        var tras = await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);

        Assert.Equal("liquidado", tras.Estado);
        Assert.Equal(600m, tras.Pagado);
        Assert.Equal(0m, tras.Saldo);
        Assert.Equal(0m, tras.PorConfirmar);
        Assert.Equal("confirmado", tras.Abonos[0].Estado);
        Assert.Equal("Caja Temixco", tras.Abonos[0].RevisadoPor);
        Assert.Equal(4, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task Confirmar_el_anticipo_deja_el_apartado_activo_con_vigencia_completa_y_sin_tocar_stock()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(1);

        var tras = await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);

        Assert.Equal("activo", tras.Estado);
        Assert.Equal(180m, tras.Pagado);
        Assert.Equal(420m, tras.Saldo);
        Assert.Equal(_reloj.Ahora.AddDays(7), tras.VenceEl);
        Assert.Equal(5, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task Despues_de_confirmar_el_anticipo_la_cajera_puede_cobrar_el_resto()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("anticipo"));
        await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);

        var tras = await _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 420m, "efectivo"));
        Assert.Equal("liquidado", tras.Estado);
        Assert.Equal(4, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task La_duena_confirma_pedidos_de_cualquier_sucursal()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra", entrega: "azteca"));
        var tras = await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, Duena);
        Assert.Equal("liquidado", tras.Estado);
        Assert.Equal("Jessica Jesly", tras.Abonos[0].RevisadoPor);
    }

    [Fact]
    public async Task La_cajera_no_confirma_pedidos_de_otra_sucursal()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra", entrega: "azteca"));

        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco));

        Assert.Equal(403, ex.StatusCode);
        var intacto = await _svc.ObtenerAsync(ap.Id);
        Assert.Equal(0m, intacto.Pagado);
        Assert.Equal(5, Stock(SucursalId.Azteca));
    }

    [Fact]
    public async Task Una_clienta_o_un_usuario_inexistente_no_pueden_confirmar_ni_rechazar()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));

        var ex1 = await Assert.ThrowsAsync<DomnaException>(() => _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, "c1"));
        Assert.Equal(403, ex1.StatusCode);
        var ex2 = await Assert.ThrowsAsync<DomnaException>(() => _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, "fantasma"));
        Assert.Equal(403, ex2.StatusCode);
        var ex3 = await Assert.ThrowsAsync<DomnaException>(() => _svc.RechazarPagoAsync(ap.Id, ap.Abonos[0].Id, "x", "c1"));
        Assert.Equal(403, ex3.StatusCode);
        Assert.Equal(0m, (await _svc.ObtenerAsync(ap.Id)).Pagado);
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_descuenta_dos_veces_el_stock()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);

        // Reintento tras perder señal: responde 200 con el estado actual, sin volver a descontar.
        var tras = await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);
        Assert.Equal("liquidado", tras.Estado);
        Assert.Equal(600m, tras.Pagado);
        Assert.Equal(4, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task Rechazar_dos_veces_responde_200_y_conserva_el_motivo()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.RechazarPagoAsync(ap.Id, ap.Abonos[0].Id, "No llegó", CajeraTemixco);

        var tras = await _svc.RechazarPagoAsync(ap.Id, ap.Abonos[0].Id, "Otro motivo", CajeraTemixco);
        Assert.Equal("activo", tras.Estado);
        Assert.Equal("rechazado", tras.Abonos[0].Estado);
        Assert.Equal("No llegó", tras.Abonos[0].MotivoRechazo);
        Assert.Equal(5, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task Confirmar_un_pedido_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.ConfirmarPagoAsync("nada", "x", Duena));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── Rechazar ──

    [Fact]
    public async Task Rechazar_deja_el_motivo_el_pedido_sigue_activo_y_el_stock_no_cambia()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));

        var tras = await _svc.RechazarPagoAsync(ap.Id, ap.Abonos[0].Id, "No llegó a la cuenta", CajeraTemixco);

        Assert.Equal("activo", tras.Estado);
        Assert.Equal("rechazado", tras.Abonos[0].Estado);
        Assert.Equal("No llegó a la cuenta", tras.Abonos[0].MotivoRechazo);
        Assert.Equal(0m, tras.Pagado);
        Assert.Equal(5, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task La_cajera_no_rechaza_pedidos_de_otra_sucursal()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra", entrega: "azteca"));
        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.RechazarPagoAsync(ap.Id, ap.Abonos[0].Id, "x", CajeraTemixco));
        Assert.Equal(403, ex.StatusCode);
    }

    // ── Cola de "por confirmar" ──

    [Fact]
    public async Task La_cajera_solo_ve_los_pedidos_por_confirmar_de_su_sucursal()
    {
        await Vestido(temixco: 10, azteca: 10);
        var enTemixco = await _svc.CrearAsync("c1", Pedido("compra", entrega: "temixco"));
        await _svc.CrearAsync("c1", Pedido("anticipo", entrega: "azteca"));

        var suya = await _svc.ListarPorConfirmarAsync(CajeraTemixco);
        Assert.Equal(enTemixco.Id, Assert.Single(suya).Id);
        Assert.Single(await _svc.ListarPorConfirmarAsync(CajeraAzteca));
        Assert.Equal(2, (await _svc.ListarPorConfirmarAsync(Duena)).Count);
    }

    [Fact]
    public async Task Lo_confirmado_rechazado_o_sin_pago_declarado_sale_de_la_cola()
    {
        await Vestido(temixco: 10, azteca: 10);
        var confirmado = await _svc.CrearAsync("c1", Pedido("compra"));
        var rechazado = await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.CrearAsync("c1", Pedido("sin_anticipo")); // sin pago declarado
        var pendiente = await _svc.CrearAsync("c1", Pedido("anticipo"));

        await _svc.ConfirmarPagoAsync(confirmado.Id, confirmado.Abonos[0].Id, CajeraTemixco);
        await _svc.RechazarPagoAsync(rechazado.Id, rechazado.Abonos[0].Id, "No llegó", CajeraTemixco);

        var cola = await _svc.ListarPorConfirmarAsync(CajeraTemixco);
        Assert.Equal(pendiente.Id, Assert.Single(cola).Id);
    }

    [Fact]
    public async Task Las_ventas_de_mostrador_nunca_pasan_por_la_cola()
    {
        await Vestido();
        var fisico = new CrearApartadoInput(new List<LineaCarritoInput> { new("v-p1", 1) }, "compra", "temixco", "fisico", null, "efectivo");
        var ap = await _svc.CrearAsync(null, fisico);

        Assert.Equal("liquidado", ap.Estado);
        Assert.Empty(await _svc.ListarPorConfirmarAsync(Duena));
    }

    [Fact]
    public async Task Un_usuario_sin_permiso_no_puede_ver_la_cola()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.ListarPorConfirmarAsync("c1"));
        Assert.Equal(403, ex.StatusCode);
    }

    // ── Cobro en mostrador y vencimiento ──

    [Fact]
    public async Task No_se_puede_cobrar_en_mostrador_un_pedido_con_pago_por_confirmar()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.AbonarAsync(new RegistrarAbonoInput(ap.Id, 600m, "efectivo")));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(5, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task Una_compra_en_linea_que_nunca_se_paga_vence_libera_el_stock_y_no_penaliza()
    {
        await Vestido(temixco: 1, azteca: 0);
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));
        Assert.Equal(1, (await _apartados.ObtenerReservadoAsync("v-p1"))["temixco"]);

        _reloj.Ahora = _reloj.Ahora.AddDays(3);
        var visto = await _svc.ObtenerAsync(ap.Id);

        Assert.Equal("vencido", visto.Estado);
        Assert.Equal(0m, _ana.PenalizacionPendiente);
        Assert.Empty(await _apartados.ObtenerReservadoAsync("v-p1"));
        Assert.Equal(1, Stock(SucursalId.Temixco));
    }

    [Fact]
    public async Task Confirmar_el_pago_de_un_pedido_que_ya_vencio_no_lo_liquida_ni_descuenta_stock()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(5, Stock(SucursalId.Temixco));
        Assert.Empty(await _svc.ListarPorConfirmarAsync(Duena));
    }

    [Fact]
    public async Task Un_apartado_con_anticipo_sin_confirmar_que_vence_si_penaliza()
    {
        await Vestido();
        await _svc.CrearAsync("c1", Pedido("anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        await _svc.ListarAsync(null, null, null);
        Assert.Equal(100m, _ana.PenalizacionPendiente);
    }

    [Fact]
    public async Task La_penalizacion_del_mes_no_cuenta_las_compras_sin_pagar()
    {
        await Vestido(temixco: 10, azteca: 10);
        await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.CrearAsync("c1", Pedido("compra"));
        await _svc.CrearAsync("c1", Pedido("sin_anticipo"));
        _reloj.Ahora = _reloj.Ahora.AddDays(3);

        var m = await _svc.MetricasAsync(null);
        Assert.Equal(100m, m.PenalizacionesMes); // solo el apartado
    }

    // ── Avisos ──

    [Fact]
    public async Task El_portal_de_la_clienta_muestra_pagado_hasta_que_la_tienda_confirma()
    {
        await Vestido();
        var ap = await _svc.CrearAsync("c1", Pedido("compra"));

        var antes = (await _svc.ListarDeClientaAsync("c1")).Single();
        Assert.Equal(0m, antes.Pagado);
        Assert.Equal(600m, antes.PorConfirmar);
        Assert.Equal("activo", antes.Estado);

        await _svc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);

        var despues = (await _svc.ListarDeClientaAsync("c1")).Single();
        Assert.Equal(600m, despues.Pagado);
        Assert.Equal(0m, despues.PorConfirmar);
        Assert.Equal("liquidado", despues.Estado);
    }
}
