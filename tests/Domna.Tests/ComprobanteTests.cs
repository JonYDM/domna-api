using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Almacenamiento en memoria: recuerda qué se "subió" y firma enlaces de mentira.</summary>
public class AlmacenFake : IAlmacenamientoImagenes
{
    public bool Disponible { get; set; } = true;
    public Dictionary<string, InfoObjeto> Subidos { get; } = new();
    public List<(string Clave, TimeSpan Vigencia)> Lecturas { get; } = new();

    public FirmaSubida FirmarSubidaProducto(string productoId, IEnumerable<string> tamanos, string contentType)
        => throw new NotImplementedException();

    public ComprobanteFirmado FirmarSubidaComprobante(string apartadoId, string contentType)
    {
        var ext = contentType == "image/png" ? "png" : contentType == "image/webp" ? "webp" : "jpg";
        var clave = $"comprobantes/{apartadoId}/{Guid.NewGuid():N}.{ext}";
        return new ComprobanteFirmado($"https://privado.test/put/{clave}", clave);
    }

    public Task<InfoObjeto?> ObtenerInfoComprobanteAsync(string clave, CancellationToken ct = default)
        => Task.FromResult(Subidos.TryGetValue(clave, out var i) ? i : null);

    public string FirmarLecturaComprobante(string clave, TimeSpan vigencia)
    {
        Lecturas.Add((clave, vigencia));
        return $"https://privado.test/get/{clave}?firma=abc";
    }
}

public class ClabeTests
{
    [Theory]
    [InlineData("012180001183597172")]
    [InlineData("072180000112345679")]
    [InlineData("146180123456789018")]
    [InlineData("0121 8000 1183 5971 72")]
    [InlineData("012-180-001183597172")]
    public void Acepta_clabes_con_digito_verificador_correcto(string clabe) => Assert.True(Clabe.EsValida(clabe));

    [Theory]
    [InlineData("012180001183597173")] // verificador mal
    [InlineData("01218000118359717")] // 17 dígitos
    [InlineData("0121800011835971720")] // 19 dígitos
    [InlineData("01218000118359717A")]
    [InlineData("")]
    [InlineData(null)]
    public void Rechaza_clabes_invalidas(string? clabe) => Assert.False(Clabe.EsValida(clabe));

    [Fact]
    public void Un_digito_cambiado_siempre_se_detecta()
    {
        const string buena = "012180001183597172";
        for (var i = 0; i < 17; i++)
        {
            var d = buena[i] - '0';
            var mala = buena.ToCharArray();
            mala[i] = (char)('0' + (d + 1) % 10);
            Assert.False(Clabe.EsValida(new string(mala)), $"posición {i}");
        }
    }
}

public class DatosBancariosTests
{
    private const string ClabeBuena = "012180001183597172";

    private static ConfigBoutique Nueva() => ConfigBoutique.CrearDefault("Jesly Boutique", "jesly");

    [Fact]
    public void Nace_sin_datos()
    {
        var c = Nueva();
        Assert.False(c.TieneDatosBancarios);
        Assert.Null(c.BancoClabe);
    }

    [Fact]
    public void Guarda_la_clabe_sin_espacios_y_limpia_el_resto()
    {
        var c = Nueva();
        c.FijarDatosBancarios("  Jessica   Jesly ", " BBVA ", "0121 8000 1183 5971 72");
        Assert.True(c.TieneDatosBancarios);
        Assert.Equal("Jessica Jesly", c.BancoTitular);
        Assert.Equal("BBVA", c.BancoNombre);
        Assert.Equal(ClabeBuena, c.BancoClabe);
    }

    [Fact]
    public void Vaciar_los_tres_borra_los_datos()
    {
        var c = Nueva();
        c.FijarDatosBancarios("Jessica", "BBVA", ClabeBuena);
        c.FijarDatosBancarios("", null, "  ");
        Assert.False(c.TieneDatosBancarios);
        Assert.Null(c.BancoTitular);
    }

    [Theory]
    [InlineData("", "BBVA", ClabeBuena)]
    [InlineData("Jessica", "", ClabeBuena)]
    [InlineData("Jessica", "BBVA", "012180001183597173")]
    [InlineData("Jessica", "BBVA", "")]
    public void Datos_incompletos_o_invalidos_se_rechazan_y_no_cambian_nada(string titular, string banco, string clabe)
    {
        var c = Nueva();
        c.FijarDatosBancarios("Original", "Banco", ClabeBuena);
        var ex = Assert.Throws<DomnaException>(() => c.FijarDatosBancarios(titular, banco, clabe));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Original", c.BancoTitular);
    }

    [Fact]
    public async Task El_servicio_los_devuelve_y_los_actualiza()
    {
        var repo = new ConfigRepoFake();
        var svc = new ConfigService(repo);

        var antes = await svc.ObtenerDatosBancariosAsync();
        Assert.False(antes.Configurados);

        var tras = await svc.FijarDatosBancariosAsync(new DatosBancariosInput("Jessica Jesly", "BBVA", ClabeBuena));
        Assert.True(tras.Configurados);
        Assert.Equal(ClabeBuena, (await svc.ObtenerDatosBancariosAsync()).Clabe);
    }

    [Fact]
    public async Task La_config_publica_no_incluye_los_datos_bancarios()
    {
        var repo = new ConfigRepoFake();
        var svc = new ConfigService(repo);
        await svc.FijarDatosBancariosAsync(new DatosBancariosInput("Jessica Jesly", "BBVA", ClabeBuena));

        var json = System.Text.Json.JsonSerializer.Serialize(await svc.ObtenerAsync());
        Assert.DoesNotContain(ClabeBuena, json);
        Assert.DoesNotContain("Jessica", json);
    }
}

public class LimitadorIntentosTests
{
    private readonly RelojFijo _reloj = new();
    private LimitadorIntentos Crear() => new(_reloj);

    [Fact]
    public void Cuenta_los_intentos_restantes_y_bloquea_al_quinto_fallo()
    {
        var l = Crear();
        Assert.Equal(4, l.RegistrarFallo("u"));
        Assert.Equal(3, l.RegistrarFallo("u"));
        Assert.Equal(2, l.RegistrarFallo("u"));
        Assert.Equal(1, l.RegistrarFallo("u"));
        Assert.Null(l.Bloqueo("u"));
        Assert.Equal(0, l.RegistrarFallo("u"));
        Assert.NotNull(l.Bloqueo("u"));
    }

    [Fact]
    public void El_bloqueo_dura_diez_minutos()
    {
        var l = Crear();
        for (var i = 0; i < 5; i++) l.RegistrarFallo("u");

        _reloj.Ahora = _reloj.Ahora.AddMinutes(9);
        Assert.NotNull(l.Bloqueo("u"));
        _reloj.Ahora = _reloj.Ahora.AddMinutes(2);
        Assert.Null(l.Bloqueo("u"));
        Assert.Equal(4, l.RegistrarFallo("u")); // empieza de cero
    }

    [Fact]
    public void Un_acierto_borra_los_fallos()
    {
        var l = Crear();
        l.RegistrarFallo("u");
        l.RegistrarFallo("u");
        l.Reiniciar("u");
        Assert.Equal(4, l.RegistrarFallo("u"));
    }

    [Fact]
    public void Los_fallos_viejos_no_se_acumulan()
    {
        var l = Crear();
        for (var i = 0; i < 4; i++) l.RegistrarFallo("u");
        _reloj.Ahora = _reloj.Ahora.AddMinutes(11);
        Assert.Equal(4, l.RegistrarFallo("u"));
        Assert.Null(l.Bloqueo("u"));
    }

    [Fact]
    public void Cada_usuario_tiene_su_propio_contador()
    {
        var l = Crear();
        for (var i = 0; i < 5; i++) l.RegistrarFallo("a");
        Assert.NotNull(l.Bloqueo("a"));
        Assert.Null(l.Bloqueo("b"));
    }
}

public class ComprobanteDominioTests
{
    private static readonly DateTime Hoy = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private int _seq;
    private string Id() => $"id{++_seq}";

    private Apartado Online(MetodoPago metodo, decimal anticipo = 600m, ModalidadApartado m = ModalidadApartado.Compra) =>
        Apartado.Crear(
            "ap1", "DOM-0001", "c1", "Ana", "7771234567",
            new List<LineaDraft> { new("v1", "p1", "Vestido", Silueta.Vestido, "M", "Rosa", "#F2A7B5", 600m, 250m, 1, SucursalId.Temixco) },
            600m, 0m, 600m, m, Canal.Online, SucursalId.Temixco, false,
            Hoy, Hoy.AddDays(2), Hoy, anticipo, metodo, Id, Id);

    [Fact]
    public void Adjuntar_deja_el_comprobante_en_el_pago_pendiente_y_pone_el_pedido_en_revision()
    {
        var a = Online(MetodoPago.Transferencia);
        Assert.True(a.AceptaComprobante);
        Assert.False(a.EnRevision);

        a.AdjuntarComprobante("comprobantes/ap1/x.jpg", Id, Hoy);

        Assert.Single(a.Abonos);
        Assert.True(a.Abonos[0].TieneComprobante);
        Assert.Equal(EstadoPago.Pendiente, a.Abonos[0].Estado);
        Assert.True(a.EnRevision);
    }

    [Fact]
    public void Volver_a_subir_mientras_esta_pendiente_reemplaza_el_archivo_sin_duplicar_el_pago()
    {
        var a = Online(MetodoPago.Transferencia);
        a.AdjuntarComprobante("comprobantes/ap1/uno.jpg", Id, Hoy);
        a.AdjuntarComprobante("comprobantes/ap1/dos.jpg", Id, Hoy);
        Assert.Single(a.Abonos);
        Assert.Equal("comprobantes/ap1/dos.jpg", a.Abonos[0].ComprobanteClave);
    }

    [Theory]
    [InlineData(MetodoPago.Efectivo)]
    [InlineData(MetodoPago.Tarjeta)]
    public void Efectivo_y_tarjeta_no_aceptan_comprobante(MetodoPago metodo)
    {
        var a = Online(metodo);
        Assert.False(a.AceptaComprobante);
        var ex = Assert.Throws<DomnaException>(() => a.AdjuntarComprobante("comprobantes/ap1/x.jpg", Id, Hoy));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Un_apartado_sin_anticipo_no_acepta_comprobante()
    {
        var a = Online(MetodoPago.Transferencia, anticipo: 0m, m: ModalidadApartado.SinAnticipo);
        Assert.False(a.AceptaComprobante);
        Assert.Throws<DomnaException>(() => a.AdjuntarComprobante("comprobantes/ap1/x.jpg", Id, Hoy));
    }

    [Fact]
    public void Tras_un_rechazo_la_clienta_puede_reintentar_con_un_pago_nuevo_por_el_mismo_monto()
    {
        var a = Online(MetodoPago.Transferencia);
        a.AdjuntarComprobante("comprobantes/ap1/uno.jpg", Id, Hoy);
        a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, "Borroso");
        Assert.True(a.AceptaComprobante);

        a.AdjuntarComprobante("comprobantes/ap1/dos.jpg", Id, Hoy.AddHours(1));

        Assert.Equal(2, a.Abonos.Count);
        Assert.Equal(EstadoPago.Rechazado, a.Abonos[0].Estado);
        Assert.Equal(EstadoPago.Pendiente, a.Abonos[1].Estado);
        Assert.Equal(600m, a.Abonos[1].Monto);
        Assert.Equal(ConceptoAbono.Compra, a.Abonos[1].Concepto);
        Assert.Equal("comprobantes/ap1/dos.jpg", a.Abonos[1].ComprobanteClave);
        Assert.Equal(600m, a.PorConfirmar);
    }

    [Fact]
    public void Despues_de_confirmar_el_pedido_ya_no_acepta_comprobantes()
    {
        var a = Online(MetodoPago.Transferencia);
        a.AdjuntarComprobante("comprobantes/ap1/uno.jpg", Id, Hoy);
        a.ConfirmarPago(a.Abonos[0].Id, "Caja", Hoy, 7);

        Assert.False(a.AceptaComprobante);
        Assert.Throws<DomnaException>(() => a.AdjuntarComprobante("comprobantes/ap1/dos.jpg", Id, Hoy));
    }

    [Fact]
    public void No_vence_mientras_la_tienda_revisa_un_comprobante()
    {
        var a = Online(MetodoPago.Transferencia);
        a.AdjuntarComprobante("comprobantes/ap1/uno.jpg", Id, Hoy);

        Assert.False(a.Vencer(Hoy.AddDays(10)));
        Assert.Equal(EstadoApartado.Activo, a.Estado);
    }

    [Fact]
    public void Si_la_tienda_rechaza_el_comprobante_el_pedido_vuelve_a_poder_vencer()
    {
        var a = Online(MetodoPago.Transferencia);
        a.AdjuntarComprobante("comprobantes/ap1/uno.jpg", Id, Hoy);
        a.RechazarPago(a.Abonos[0].Id, "Caja", Hoy, "No llegó");

        a.Vencer(Hoy.AddDays(10));
        Assert.Equal(EstadoApartado.Vencido, a.Estado);
    }

    [Fact]
    public void Sin_comprobante_el_pedido_vence_normal()
    {
        var a = Online(MetodoPago.Transferencia);
        a.Vencer(Hoy.AddDays(3));
        Assert.Equal(EstadoApartado.Vencido, a.Estado);
    }
}
