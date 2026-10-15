using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Pagos declarados en línea: nacen por confirmar y solo cuentan cuando la tienda los confirma.</summary>
public class PagoPendienteTests
{
    private static readonly DateTime Hoy = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private int _seq;
    private string Id() => $"id{++_seq}";

    private static LineaDraft Linea() =>
        new("v1", "p1", "Vestido", Silueta.Vestido, "M", "Rosa", "#F2A7B5", 600m, 250m, 1, SucursalId.Temixco);

    private Apartado Online(ModalidadApartado modalidad, decimal anticipo, int venceEnDias = 2) =>
        Apartado.Crear(
            "ap1", "DOM-0001", "c1", "Ana Lopez", "7771234567",
            new List<LineaDraft> { Linea() },
            600m, 0m, 600m, modalidad, Canal.Online, SucursalId.Temixco, false,
            Hoy, Hoy.AddDays(venceEnDias), Hoy, anticipo, MetodoPago.Efectivo, Id, Id);

    // ── Nacimiento ──

    [Fact]
    public void Compra_en_linea_no_se_liquida_sola_y_el_pago_queda_por_confirmar()
    {
        var a = Online(ModalidadApartado.Compra, 600m);

        Assert.Equal(EstadoApartado.Activo, a.Estado);
        Assert.Null(a.CerradoEl);
        Assert.Equal(0m, a.Pagado);
        Assert.Equal(600m, a.PorConfirmar);
        Assert.Equal(600m, a.Saldo);
        Assert.True(a.TienePagoPendiente);
        var abono = Assert.Single(a.Abonos);
        Assert.Equal(EstadoPago.Pendiente, abono.Estado);
        Assert.Equal(ConceptoAbono.Compra, abono.Concepto);
    }

    [Fact]
    public void Anticipo_en_linea_tampoco_cuenta_como_pagado_hasta_confirmarse()
    {
        var a = Online(ModalidadApartado.Anticipo, 180m);
        Assert.Equal(0m, a.Pagado);
        Assert.Equal(180m, a.PorConfirmar);
        Assert.Equal(600m, a.Saldo);
    }

    [Fact]
    public void Apartado_en_linea_sin_anticipo_no_tiene_ningun_pago_pendiente()
    {
        var a = Online(ModalidadApartado.SinAnticipo, 0m);
        Assert.Empty(a.Abonos);
        Assert.False(a.TienePagoPendiente);
    }

    [Fact]
    public void En_mostrador_el_pago_nace_confirmado()
    {
        var a = Apartado.Crear(
            "ap1", "DOM-0001", "c1", "Ana", "7771234567", new List<LineaDraft> { Linea() },
            600m, 0m, 600m, ModalidadApartado.Compra, Canal.Fisico, SucursalId.Temixco, false,
            Hoy, Hoy, Hoy, 600m, MetodoPago.Tarjeta, Id, Id);

        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(EstadoPago.Confirmado, a.Abonos[0].Estado);
        Assert.Equal(600m, a.Pagado);
    }

    // ── Confirmar ──

    [Fact]
    public void Confirmar_el_pago_de_una_compra_la_liquida_y_deja_quien_la_confirmo()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        var ahora = Hoy.AddHours(5);

        a.ConfirmarPago(a.Abonos[0].Id, "Caja Temixco", ahora, 7);

        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(ahora, a.CerradoEl);
        Assert.Equal(600m, a.Pagado);
        Assert.Equal(0m, a.Saldo);
        Assert.Equal(0m, a.PorConfirmar);
        Assert.Equal(EstadoPago.Confirmado, a.Abonos[0].Estado);
        Assert.Equal("Caja Temixco", a.Abonos[0].RevisadoPor);
        Assert.Equal(ahora, a.Abonos[0].RevisadoEl);
    }

    [Fact]
    public void Confirmar_el_anticipo_deja_el_apartado_activo_y_empieza_la_vigencia_completa()
    {
        var a = Online(ModalidadApartado.Anticipo, 180m, venceEnDias: 2);
        var dia = Hoy.AddDays(1);

        a.ConfirmarPago(a.Abonos[0].Id, "Caja", dia, 7);

        Assert.Equal(EstadoApartado.Activo, a.Estado);
        Assert.Equal(180m, a.Pagado);
        Assert.Equal(420m, a.Saldo);
        Assert.Equal(dia.AddDays(7), a.VenceEl);
    }

    [Fact]
    public void Confirmar_nunca_acorta_la_vigencia()
    {
        var a = Online(ModalidadApartado.Anticipo, 180m, venceEnDias: 30);
        a.ConfirmarPago(a.Abonos[0].Id, "Caja", Hoy, 7);
        Assert.Equal(Hoy.AddDays(30), a.VenceEl);
    }

    [Fact]
    public void Confirmar_dos_veces_no_duplica_el_pago_ni_cambia_quien_reviso()
    {
        var a = Online(ModalidadApartado.Anticipo, 180m);
        a.ConfirmarPago(a.Abonos[0].Id, "Caja", Hoy, 7);
        var revisadoPor = a.Abonos[0].RevisadoPor;
        var revisadoEl = a.Abonos[0].RevisadoEl;

        // Reintento tras perder señal: no lanza error ni cambia nada.
        a.ConfirmarPago(a.Abonos[0].Id, "Otra Caja", Hoy.AddDays(1), 7);

        Assert.Equal(180m, a.Pagado); // no se duplicó
        Assert.Equal(EstadoPago.Confirmado, a.Abonos[0].Estado);
        Assert.Equal(revisadoPor, a.Abonos[0].RevisadoPor); // mismo revisor
        Assert.Equal(revisadoEl, a.Abonos[0].RevisadoEl);
    }

    [Fact]
    public void Confirmar_una_compra_dos_veces_no_la_vuelve_a_liquidar_ni_cambia_el_cierre()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        var ahora = Hoy.AddHours(5);
        a.ConfirmarPago(a.Abonos[0].Id, "Caja Temixco", ahora, 7);
        var cerrado = a.CerradoEl;

        // Reintento: sigue liquidado, mismo cierre, mismo revisor.
        a.ConfirmarPago(a.Abonos[0].Id, "Otra Caja", Hoy.AddDays(1), 7);

        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(cerrado, a.CerradoEl);
        Assert.Equal("Caja Temixco", a.Abonos[0].RevisadoPor);
    }

    [Fact]
    public void Rechazar_dos_veces_conserva_el_motivo_original()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, "No llegó");

        // Reintento: no lanza error ni cambia el motivo ni el revisor.
        a.RechazarPago(a.Abonos[0].Id, "Otra Caja", Hoy.AddDays(1), "Otro motivo");

        Assert.Equal(EstadoPago.Rechazado, a.Abonos[0].Estado);
        Assert.Equal("No llegó", a.Abonos[0].MotivoRechazo);
        Assert.Equal("Caja", a.Abonos[0].RevisadoPor);
    }

    [Fact]
    public void Confirmar_un_pago_inexistente_responde_404()
    {
        var a = Online(ModalidadApartado.Anticipo, 180m);
        var ex = Assert.Throws<DomnaException>(() => a.ConfirmarPago("nada", "Caja", Hoy, 7));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void No_se_confirma_el_pago_de_un_pedido_ya_vencido_o_cancelado()
    {
        var vencido = Online(ModalidadApartado.Compra, 600m);
        vencido.Vencer(Hoy.AddDays(3));
        var ex = Assert.Throws<DomnaException>(() => vencido.ConfirmarPago(vencido.Abonos[0].Id, "Caja", Hoy.AddDays(3), 7));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0m, vencido.Pagado);

        var cancelado = Online(ModalidadApartado.Compra, 600m);
        cancelado.Cancelar(Hoy);
        Assert.Throws<DomnaException>(() => cancelado.ConfirmarPago(cancelado.Abonos[0].Id, "Caja", Hoy, 7));
    }

    // ── Rechazar ──

    [Fact]
    public void Rechazar_deja_el_motivo_no_cuenta_como_pagado_y_el_pedido_sigue_activo()
    {
        var a = Online(ModalidadApartado.Compra, 600m);

        a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, "  No llegó a la cuenta  ");

        Assert.Equal(EstadoPago.Rechazado, a.Abonos[0].Estado);
        Assert.Equal("No llegó a la cuenta", a.Abonos[0].MotivoRechazo);
        Assert.Equal("Caja", a.Abonos[0].RevisadoPor);
        Assert.Equal(0m, a.Pagado);
        Assert.Equal(0m, a.PorConfirmar);
        Assert.Equal(600m, a.Saldo);
        Assert.Equal(EstadoApartado.Activo, a.Estado);
        Assert.False(a.TienePagoPendiente);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rechazar_exige_motivo(string motivo)
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        var ex = Assert.Throws<DomnaException>(() => a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, motivo));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(EstadoPago.Pendiente, a.Abonos[0].Estado);
    }

    [Fact]
    public void Un_pago_rechazado_no_se_puede_confirmar_despues()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, "No llegó");
        Assert.Throws<DomnaException>(() => a.ConfirmarPago(a.Abonos[0].Id, "Caja", Hoy, 7));
        Assert.Equal(0m, a.Pagado);
    }

    // ── Cobro en mostrador sobre un pedido con pago pendiente ──

    [Fact]
    public void No_se_registra_otro_abono_mientras_haya_un_pago_por_confirmar()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        var ex = Assert.Throws<DomnaException>(() => a.Abonar(Id, 600m, Hoy, MetodoPago.Efectivo, 7));
        Assert.Equal(400, ex.StatusCode);
        Assert.Single(a.Abonos); // evita cobrar dos veces el mismo pedido
    }

    [Fact]
    public void Tras_rechazar_ya_se_puede_cobrar_en_mostrador()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, "No llegó");

        a.Abonar(Id, 600m, Hoy, MetodoPago.Efectivo, 7);

        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(600m, a.Pagado);
    }

    // ── Vencimiento ──

    [Fact]
    public void Una_compra_en_linea_sin_pagar_vence_sin_penalizacion()
    {
        var a = Online(ModalidadApartado.Compra, 600m);
        var genero = a.Vencer(Hoy.AddDays(3));
        Assert.False(genero);
        Assert.Equal(EstadoApartado.Vencido, a.Estado);
        Assert.False(a.Penalizado);
    }

    [Fact]
    public void Un_apartado_con_anticipo_sin_confirmar_si_penaliza_al_vencer()
    {
        var a = Online(ModalidadApartado.Anticipo, 180m);
        Assert.True(a.Vencer(Hoy.AddDays(3)));
        Assert.Equal(EstadoApartado.Vencido, a.Estado);
    }
}
