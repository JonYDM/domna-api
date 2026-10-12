using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Reglas del agregado Apartado (sin repositorios ni servicios).</summary>
public class ApartadoTests
{
    private static readonly DateTime Hoy = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private int _seq;
    private string Id() => $"id{++_seq}";

    private static LineaDraft Linea(decimal precio = 600m, decimal costo = 250m, int cantidad = 1,
        SucursalId origen = SucursalId.Temixco) =>
        new("v1", "p1", "Vestido", Silueta.Vestido, "M", "Rosa", "#F2A7B5", precio, costo, cantidad, origen);

    private Apartado Nuevo(
        ModalidadApartado modalidad = ModalidadApartado.SinAnticipo,
        decimal total = 600m,
        decimal anticipo = 0m,
        bool traslado = false,
        int venceEnDias = 2,
        List<LineaDraft>? lineas = null) =>
        Apartado.Crear(
            "ap1", "DOM-0001", "c1", "Ana Lopez", "7771234567",
            lineas ?? new List<LineaDraft> { Linea() },
            subtotal: total, penalizacion: 0m, total: total,
            modalidad, Canal.Online, SucursalId.Temixco, traslado,
            Hoy, Hoy.AddDays(venceEnDias), Hoy, anticipo, MetodoPago.Efectivo, Id, Id);

    // ── Crear ──

    [Fact]
    public void Crear_sin_lineas_se_rechaza()
    {
        var ex = Assert.Throws<DomnaException>(() => Nuevo(lineas: new List<LineaDraft>()));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Crear_sin_anticipo_queda_activo_sin_abonos()
    {
        var a = Nuevo();
        Assert.Equal(EstadoApartado.Activo, a.Estado);
        Assert.Empty(a.Abonos);
        Assert.Equal(0, a.Pagado);
        Assert.Equal(600m, a.Saldo);
    }

    [Fact]
    public void Crear_con_anticipo_registra_el_primer_abono_como_anticipo()
    {
        var a = Nuevo(ModalidadApartado.Anticipo, anticipo: 180m);
        var abono = Assert.Single(a.Abonos);
        Assert.Equal(ConceptoAbono.Anticipo, abono.Concepto);
        Assert.Equal(180m, a.Pagado);
        Assert.Equal(420m, a.Saldo);
        Assert.Equal(EstadoApartado.Activo, a.Estado);
    }

    [Fact]
    public void Crear_compra_se_liquida_sola_y_el_abono_es_concepto_compra()
    {
        var a = Nuevo(ModalidadApartado.Compra, anticipo: 600m);
        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(Hoy, a.CerradoEl);
        Assert.Equal(0m, a.Saldo);
        Assert.Equal(ConceptoAbono.Compra, Assert.Single(a.Abonos).Concepto);
    }

    [Fact]
    public void Crear_con_traslado_nace_en_origen_y_sin_traslado_nace_listo()
    {
        Assert.Equal(EstadoEntrega.EnOrigen, Nuevo(traslado: true).EstadoEntrega);
        Assert.Equal(EstadoEntrega.Listo, Nuevo(traslado: false).EstadoEntrega);
    }

    [Fact]
    public void Piezas_y_ganancia_suman_todas_las_lineas()
    {
        var a = Nuevo(total: 1500m, lineas: new List<LineaDraft>
        {
            Linea(precio: 600m, costo: 250m, cantidad: 2),
            Linea(precio: 300m, costo: 100m, cantidad: 1),
        });
        Assert.Equal(3, a.Piezas);
        Assert.Equal((600m - 250m) * 2 + (300m - 100m), a.Ganancia);
    }

    // ── Abonar ──

    [Fact]
    public void Abono_parcial_en_sin_anticipo_pasa_a_anticipo_y_extiende_la_vigencia()
    {
        var a = Nuevo(venceEnDias: 2);
        var manana = Hoy.AddDays(1);

        a.Abonar(Id, 100m, manana, MetodoPago.Efectivo, vigenciaConAnticipoDias: 7);

        Assert.Equal(ModalidadApartado.Anticipo, a.Modalidad);
        Assert.Equal(manana.AddDays(7), a.VenceEl);
        Assert.Equal(EstadoApartado.Activo, a.Estado);
        Assert.Equal(500m, a.Saldo);
        Assert.Equal(ConceptoAbono.Abono, a.Abonos.Last().Concepto);
    }

    [Fact]
    public void Abono_parcial_en_anticipo_no_cambia_la_vigencia()
    {
        var a = Nuevo(ModalidadApartado.Anticipo, anticipo: 180m, venceEnDias: 7);
        var vence = a.VenceEl;

        a.Abonar(Id, 100m, Hoy.AddDays(1), MetodoPago.Tarjeta, 7);

        Assert.Equal(vence, a.VenceEl);
        Assert.Equal(320m, a.Saldo);
    }

    [Fact]
    public void Abono_por_el_saldo_exacto_liquida()
    {
        var a = Nuevo(ModalidadApartado.Anticipo, anticipo: 180m);
        var dia = Hoy.AddDays(3);

        a.Abonar(Id, 420m, dia, MetodoPago.Efectivo, 7);

        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(dia, a.CerradoEl);
        Assert.Equal(0m, a.Saldo);
        Assert.Equal(ConceptoAbono.Liquidacion, a.Abonos.Last().Concepto);
    }

    [Fact]
    public void Liquidar_sin_anticipo_de_golpe_no_cambia_la_modalidad()
    {
        var a = Nuevo();
        a.Abonar(Id, 600m, Hoy, MetodoPago.Efectivo, 7);
        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
        Assert.Equal(ModalidadApartado.SinAnticipo, a.Modalidad);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(600.01)]
    public void Abono_con_monto_invalido_se_rechaza(double monto)
    {
        var a = Nuevo();
        var ex = Assert.Throws<DomnaException>(() =>
            a.Abonar(Id, (decimal)monto, Hoy, MetodoPago.Efectivo, 7));
        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(a.Abonos);
    }

    [Fact]
    public void Abono_con_pagos_previos_no_puede_exceder_el_saldo_restante()
    {
        var a = Nuevo();
        a.Abonar(Id, 500m, Hoy, MetodoPago.Efectivo, 7);
        Assert.Throws<DomnaException>(() => a.Abonar(Id, 101m, Hoy, MetodoPago.Efectivo, 7));
        a.Abonar(Id, 100m, Hoy, MetodoPago.Efectivo, 7);
        Assert.Equal(EstadoApartado.Liquidado, a.Estado);
    }

    [Fact]
    public void No_se_abona_a_un_apartado_cerrado()
    {
        var cancelado = Nuevo();
        cancelado.Cancelar(Hoy);
        Assert.Throws<DomnaException>(() => cancelado.Abonar(Id, 100m, Hoy, MetodoPago.Efectivo, 7));

        var liquidado = Nuevo(ModalidadApartado.Compra, anticipo: 600m);
        Assert.Throws<DomnaException>(() => liquidado.Abonar(Id, 1m, Hoy, MetodoPago.Efectivo, 7));
    }

    // ── Cancelar ──

    [Fact]
    public void Cancelar_cambia_el_estado_y_conserva_los_pagos()
    {
        var a = Nuevo(ModalidadApartado.Anticipo, anticipo: 180m);
        a.Cancelar(Hoy.AddDays(1));
        Assert.Equal(EstadoApartado.Cancelado, a.Estado);
        Assert.Equal(Hoy.AddDays(1), a.CerradoEl);
        Assert.Equal(180m, a.Pagado); // el anticipo no se devuelve
    }

    [Fact]
    public void Solo_se_cancela_un_apartado_activo()
    {
        var a = Nuevo();
        a.Cancelar(Hoy);
        Assert.Throws<DomnaException>(() => a.Cancelar(Hoy));

        var liquidado = Nuevo(ModalidadApartado.Compra, anticipo: 600m);
        Assert.Throws<DomnaException>(() => liquidado.Cancelar(Hoy));
    }

    // ── Vencer ──

    [Fact]
    public void Vencer_antes_de_la_fecha_no_hace_nada()
    {
        var a = Nuevo(venceEnDias: 2);
        Assert.False(a.Vencer(Hoy.AddDays(1)));
        Assert.Equal(EstadoApartado.Activo, a.Estado);
        Assert.Null(a.CerradoEl);
    }

    [Fact]
    public void Vencer_en_la_fecha_exacta_vence_y_genera_penalizacion()
    {
        var a = Nuevo(venceEnDias: 2);
        var genero = a.Vencer(Hoy.AddDays(2));
        Assert.True(genero);
        Assert.Equal(EstadoApartado.Vencido, a.Estado);
        Assert.True(a.Penalizado);
        Assert.Equal(a.VenceEl, a.CerradoEl); // se cierra en la fecha de vencimiento, no cuando se detecta
    }

    [Fact]
    public void Vencer_es_idempotente_y_no_duplica_penalizacion()
    {
        var a = Nuevo(venceEnDias: 2);
        Assert.True(a.Vencer(Hoy.AddDays(5)));
        Assert.False(a.Vencer(Hoy.AddDays(6)));
        Assert.False(a.Vencer(Hoy.AddDays(7)));
        Assert.Equal(EstadoApartado.Vencido, a.Estado);
    }

    [Fact]
    public void Un_apartado_liquidado_o_cancelado_nunca_vence()
    {
        var liquidado = Nuevo(ModalidadApartado.Compra, anticipo: 600m);
        Assert.False(liquidado.Vencer(Hoy.AddDays(30)));
        Assert.Equal(EstadoApartado.Liquidado, liquidado.Estado);

        var cancelado = Nuevo();
        cancelado.Cancelar(Hoy);
        Assert.False(cancelado.Vencer(Hoy.AddDays(30)));
        Assert.Equal(EstadoApartado.Cancelado, cancelado.Estado);
    }

    [Fact]
    public void Un_abono_parcial_tardio_que_extiende_vigencia_evita_el_vencimiento_original()
    {
        var a = Nuevo(venceEnDias: 2);
        a.Abonar(Id, 100m, Hoy.AddDays(1), MetodoPago.Efectivo, 7); // vence ahora en d+8
        Assert.False(a.Vencer(Hoy.AddDays(3)));
        Assert.Equal(EstadoApartado.Activo, a.Estado);
    }

    // ── Entrega ──

    [Fact]
    public void No_se_entrega_un_apartado_sin_liquidar()
    {
        var a = Nuevo();
        var ex = Assert.Throws<DomnaException>(() => a.AvanzarEntrega(EstadoEntrega.Entregado));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Se_puede_avanzar_traslado_y_marcar_listo_con_el_apartado_activo()
    {
        var a = Nuevo(traslado: true);
        a.AvanzarEntrega(EstadoEntrega.EnTraslado);
        Assert.Equal(EstadoEntrega.EnTraslado, a.EstadoEntrega);
        a.AvanzarEntrega(EstadoEntrega.Listo);
        Assert.Equal(EstadoEntrega.Listo, a.EstadoEntrega);
    }

    [Fact]
    public void Liquidado_se_puede_entregar()
    {
        var a = Nuevo(ModalidadApartado.Compra, anticipo: 600m);
        a.AvanzarEntrega(EstadoEntrega.Entregado);
        Assert.Equal(EstadoEntrega.Entregado, a.EstadoEntrega);
    }

    [Fact]
    public void No_se_avanza_la_entrega_de_un_apartado_cancelado_o_vencido()
    {
        var cancelado = Nuevo();
        cancelado.Cancelar(Hoy);
        Assert.Throws<DomnaException>(() => cancelado.AvanzarEntrega(EstadoEntrega.Listo));

        var vencido = Nuevo(venceEnDias: 1);
        vencido.Vencer(Hoy.AddDays(2));
        Assert.Throws<DomnaException>(() => vencido.AvanzarEntrega(EstadoEntrega.Listo));
    }
}
