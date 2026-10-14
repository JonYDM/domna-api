using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Subir el comprobante (clienta) y abrirlo con PIN (personal), con aislamiento por dueño y sucursal.</summary>
public class ComprobanteServiceTests
{
    private const string Pin = "123456";
    private const string Duena = "u-duena";
    private const string CajeraTemixco = "u-caja-t";
    private const string CajeraAzteca = "u-caja-a";

    private readonly RelojFijo _reloj = new();
    private readonly ProductoRepoFake _productos = new();
    private readonly ApartadoRepoFake _apartados = new();
    private readonly AlmacenFake _almacen = new();
    private readonly LimitadorIntentos _limitador;
    private readonly ApartadoService _apartadoSvc;
    private readonly ComprobanteService _svc;

    public ComprobanteServiceTests()
    {
        var ahora = _reloj.Ahora;
        var hasher = new HasherFake();
        var clientas = new ClientaRepoFake(
            Clienta.Crear("c1", "Ana", "Lopez", "7771234567", "ana@correo.com", OrigenClienta.Registro, ahora, hasher.Hash(Pin)),
            Clienta.Crear("c2", "Luisa", "Perez", "7779876543", "luisa@correo.com", OrigenClienta.Registro, ahora, hasher.Hash(Pin)),
            Clienta.CrearStaff(Duena, "Jessica", "Jesly", "7774602303", "j@x.mx", Rol.Duena, hasher.Hash(Pin), ahora),
            Clienta.CrearStaff(CajeraTemixco, "Caja", "Temixco", "7771004050", "ct@x.mx", Rol.Cajera, hasher.Hash(Pin), ahora, SucursalId.Temixco),
            Clienta.CrearStaff(CajeraAzteca, "Caja", "Azteca", "7771004051", "ca@x.mx", Rol.Cajera, hasher.Hash(Pin), ahora, SucursalId.Azteca));

        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { 10 } },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = new[] { 10 } },
        };
        _productos.AgregarAsync(Producto.Crear(
            "p1", "Vestido p1", null, "vestidos", Silueta.Vestido, 600m, 250m, true,
            new[] { new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa) },
            new[] { "M" }, stock, ahora, nuevoIdColor: () => "col", nuevoIdVariante: () => "v-p1")).GetAwaiter().GetResult();

        _limitador = new LimitadorIntentos(_reloj);
        _apartadoSvc = new ApartadoService(_apartados, _productos, clientas, new ConfigRepoFake(), _reloj);
        _svc = new ComprobanteService(_apartados, clientas, _almacen, hasher, _limitador, _reloj);
    }

    private Task<ApartadoDto> Pedido(string metodo = "transferencia", string entrega = "temixco", string clienta = "c1", string modalidad = "compra") =>
        _apartadoSvc.CrearAsync(clienta, new CrearApartadoInput(
            new List<LineaCarritoInput> { new("v-p1", 1) }, modalidad, entrega, "online", clienta, metodo));

    /// <summary>La clienta firma, "sube" el archivo y lo registra.</summary>
    private async Task<(ApartadoDto Pedido, string Clave)> ConComprobante(
        string entrega = "temixco", long bytes = 200_000, string tipo = "image/jpeg")
    {
        var ap = await Pedido(entrega: entrega);
        var firma = await _svc.FirmarSubidaAsync(ap.Id, tipo, "c1");
        _almacen.Subidos[firma.Clave] = new InfoObjeto(bytes, tipo);
        var tras = await _svc.RegistrarAsync(ap.Id, firma.Clave, "c1");
        return (tras, firma.Clave);
    }

    // ── Subir (clienta) ──

    [Fact]
    public async Task La_clienta_sube_su_comprobante_y_el_pago_queda_en_revision()
    {
        var (tras, _) = await ConComprobante();

        var abono = Assert.Single(tras.Abonos);
        Assert.True(abono.TieneComprobante);
        Assert.Equal("pendiente", abono.Estado);
        Assert.Equal(0m, tras.Pagado);
    }

    [Fact]
    public async Task El_dto_nunca_expone_la_llave_del_archivo()
    {
        var (tras, clave) = await ConComprobante();
        var json = System.Text.Json.JsonSerializer.Serialize(tras);
        Assert.DoesNotContain(clave, json);
        Assert.DoesNotContain("comprobantes/", json);
    }

    [Fact]
    public async Task La_firma_apunta_a_la_carpeta_privada_del_pedido()
    {
        var ap = await Pedido();
        var firma = await _svc.FirmarSubidaAsync(ap.Id, "image/png", "c1");
        Assert.StartsWith($"comprobantes/{ap.Id}/", firma.Clave);
        Assert.EndsWith(".png", firma.Clave);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("")]
    public async Task Solo_acepta_jpg_png_o_webp(string tipo)
    {
        var ap = await Pedido();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.FirmarSubidaAsync(ap.Id, tipo, "c1"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Otra_clienta_no_puede_firmar_ni_registrar_sobre_un_pedido_ajeno()
    {
        var ap = await Pedido();

        var ex1 = await Assert.ThrowsAsync<DomnaException>(() => _svc.FirmarSubidaAsync(ap.Id, "image/jpeg", "c2"));
        Assert.Equal(404, ex1.StatusCode);

        var ex2 = await Assert.ThrowsAsync<DomnaException>(() => _svc.RegistrarAsync(ap.Id, $"comprobantes/{ap.Id}/x.jpg", "c2"));
        Assert.Equal(404, ex2.StatusCode);
    }

    [Theory]
    [InlineData("efectivo")]
    [InlineData("tarjeta")]
    public async Task Un_pedido_de_efectivo_o_tarjeta_no_recibe_comprobante(string metodo)
    {
        var ap = await Pedido(metodo);
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.FirmarSubidaAsync(ap.Id, "image/jpeg", "c1"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task No_se_puede_registrar_la_llave_de_otro_pedido()
    {
        var mio = await Pedido();
        var otro = await Pedido();
        var firmaAjena = await _svc.FirmarSubidaAsync(otro.Id, "image/jpeg", "c1");
        _almacen.Subidos[firmaAjena.Clave] = new InfoObjeto(1000, "image/jpeg");

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.RegistrarAsync(mio.Id, firmaAjena.Clave, "c1"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("productos/p1/foto.webp")]
    [InlineData("comprobantes/otro/x.jpg")]
    public async Task Rechaza_llaves_que_no_son_de_este_pedido(string clave)
    {
        var ap = await Pedido();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.RegistrarAsync(ap.Id, clave, "c1"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Rechaza_llaves_con_salto_de_carpeta()
    {
        var ap = await Pedido();
        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.RegistrarAsync(ap.Id, $"comprobantes/{ap.Id}/../../productos/p1/foto.webp", "c1"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Si_el_archivo_no_llego_a_almacenarse_no_se_registra()
    {
        var ap = await Pedido();
        var firma = await _svc.FirmarSubidaAsync(ap.Id, "image/jpeg", "c1");

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.RegistrarAsync(ap.Id, firma.Clave, "c1"));
        Assert.Equal(400, ex.StatusCode);
        Assert.False((await _apartadoSvc.ObtenerAsync(ap.Id)).Abonos[0].TieneComprobante);
    }

    [Fact]
    public async Task Rechaza_archivos_muy_pesados_o_de_otro_tipo_aunque_ya_esten_subidos()
    {
        var ap = await Pedido();
        var firma = await _svc.FirmarSubidaAsync(ap.Id, "image/jpeg", "c1");

        _almacen.Subidos[firma.Clave] = new InfoObjeto(ComprobanteService.MaxBytes + 1, "image/jpeg");
        await Assert.ThrowsAsync<DomnaException>(() => _svc.RegistrarAsync(ap.Id, firma.Clave, "c1"));

        _almacen.Subidos[firma.Clave] = new InfoObjeto(1000, "text/html");
        await Assert.ThrowsAsync<DomnaException>(() => _svc.RegistrarAsync(ap.Id, firma.Clave, "c1"));

        _almacen.Subidos[firma.Clave] = new InfoObjeto(ComprobanteService.MaxBytes, "image/jpeg");
        var ok = await _svc.RegistrarAsync(ap.Id, firma.Clave, "c1");
        Assert.True(ok.Abonos[0].TieneComprobante);
    }

    [Fact]
    public async Task Sin_almacenamiento_configurado_responde_503()
    {
        var ap = await Pedido();
        _almacen.Disponible = false;
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.FirmarSubidaAsync(ap.Id, "image/jpeg", "c1"));
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task Tras_un_rechazo_puede_subir_otro_y_aparece_un_pago_nuevo_en_revision()
    {
        var (primero, _) = await ConComprobante();
        var cola = await _apartadoSvc.RechazarPagoAsync(primero.Id, primero.Abonos[0].Id, "Borroso", CajeraTemixco);
        Assert.Equal("rechazado", cola.Abonos[0].Estado);

        var firma = await _svc.FirmarSubidaAsync(primero.Id, "image/jpeg", "c1");
        _almacen.Subidos[firma.Clave] = new InfoObjeto(1000, "image/jpeg");
        var tras = await _svc.RegistrarAsync(primero.Id, firma.Clave, "c1");

        Assert.Equal(2, tras.Abonos.Count);
        Assert.Equal("pendiente", tras.Abonos[1].Estado);
        Assert.Equal(0m, tras.Pagado);
        Assert.Equal(600m, tras.PorConfirmar);
        // Y la cajera lo ve de nuevo en su cola.
        Assert.Single(await _apartadoSvc.ListarPorConfirmarAsync(CajeraTemixco));
    }

    // ── Ver (personal con PIN) ──

    [Fact]
    public async Task La_cajera_de_la_sucursal_abre_el_comprobante_con_su_pin_y_el_enlace_dura_dos_minutos()
    {
        var (ap, clave) = await ConComprobante();

        var vista = await _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, CajeraTemixco);

        Assert.Contains(clave, vista.Url);
        Assert.Equal(120, vista.ExpiraEnSegundos);
        var lectura = Assert.Single(_almacen.Lecturas);
        Assert.Equal(clave, lectura.Clave);
        Assert.Equal(TimeSpan.FromSeconds(120), lectura.Vigencia);
    }

    [Fact]
    public async Task La_duena_puede_abrir_comprobantes_de_cualquier_sucursal()
    {
        var (ap, _) = await ConComprobante(entrega: "azteca");
        var vista = await _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, Duena);
        Assert.NotEmpty(vista.Url);
    }

    [Fact]
    public async Task Con_pin_incorrecto_no_se_firma_ningun_enlace_y_avisa_cuantos_intentos_quedan()
    {
        var (ap, _) = await ConComprobante();

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, "000000", CajeraTemixco));

        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("4 intentos", ex.Message);
        Assert.Empty(_almacen.Lecturas);
    }

    [Fact]
    public async Task Pin_vacio_cuenta_como_fallo()
    {
        var (ap, _) = await ConComprobante();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, "", CajeraTemixco));
        Assert.Equal(403, ex.StatusCode);
        Assert.Empty(_almacen.Lecturas);
    }

    [Fact]
    public async Task Tras_cinco_fallos_se_bloquea_aunque_el_sexto_pin_sea_correcto()
    {
        var (ap, _) = await ConComprobante();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, "000000", CajeraTemixco));

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, CajeraTemixco));
        Assert.Equal(429, ex.StatusCode);
        Assert.Empty(_almacen.Lecturas);

        _reloj.Ahora = _reloj.Ahora.AddMinutes(11);
        var vista = await _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, CajeraTemixco);
        Assert.NotEmpty(vista.Url);
    }

    [Fact]
    public async Task Un_acierto_reinicia_el_contador()
    {
        var (ap, _) = await ConComprobante();
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, "000000", CajeraTemixco));
        await _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, CajeraTemixco);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, "000000", CajeraTemixco));
        Assert.Contains("4 intentos", ex.Message);
    }

    [Fact]
    public async Task El_bloqueo_es_por_persona_no_afecta_a_otra_cajera()
    {
        var (ap, _) = await ConComprobante();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, "000000", CajeraTemixco));

        var vista = await _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, Duena);
        Assert.NotEmpty(vista.Url);
    }

    [Fact]
    public async Task La_cajera_de_otra_sucursal_no_puede_abrirlo_y_no_gasta_intentos_de_pin()
    {
        var (ap, _) = await ConComprobante(entrega: "temixco");

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, CajeraAzteca));
        Assert.Equal(403, ex.StatusCode);
        Assert.Empty(_almacen.Lecturas);
        Assert.Null(_limitador.Bloqueo($"pin-comprobante:{CajeraAzteca}"));
        Assert.Equal(4, _limitador.RegistrarFallo($"pin-comprobante:{CajeraAzteca}")); // sigue con los 5 intentos
    }

    [Fact]
    public async Task Una_clienta_no_puede_ver_comprobantes_ni_con_el_pin_correcto()
    {
        var (ap, _) = await ConComprobante();
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, "c1"));
        Assert.Equal(403, ex.StatusCode);
        Assert.Empty(_almacen.Lecturas);
    }

    [Fact]
    public async Task Un_pago_sin_comprobante_o_un_pedido_inexistente_responde_404()
    {
        var sin = await Pedido();
        var ex1 = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync(sin.Id, sin.Abonos[0].Id, Pin, Duena));
        Assert.Equal(404, ex1.StatusCode);

        var ex2 = await Assert.ThrowsAsync<DomnaException>(() => _svc.VerAsync("nada", "x", Pin, Duena));
        Assert.Equal(404, ex2.StatusCode);
    }

    [Fact]
    public async Task Una_cajera_desactivada_ya_no_puede_abrir_comprobantes()
    {
        var (ap, _) = await ConComprobante();
        // Se simula desactivarla usando un repositorio donde esa cuenta está inactiva.
        var hasher = new HasherFake();
        var inactiva = Clienta.CrearStaff("u-baja", "Caja", "Baja", "7771004052", "cb@x.mx", Rol.Cajera, hasher.Hash(Pin), _reloj.Ahora, SucursalId.Temixco);
        inactiva.Desactivar();
        var svc = new ComprobanteService(_apartados, new ClientaRepoFake(inactiva), _almacen, hasher, _limitador, _reloj);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, "u-baja"));
        Assert.Equal(403, ex.StatusCode);
    }

    // ── Flujo completo ──

    [Fact]
    public async Task Flujo_completo_transferencia_comprobante_pin_y_confirmacion()
    {
        // 1. La clienta pide un vestido por transferencia y sube su comprobante.
        var (ap, _) = await ConComprobante();
        Assert.Equal("activo", ap.Estado);

        // 2. La cajera lo ve en su cola, abre el comprobante con su PIN y confirma.
        var cola = await _apartadoSvc.ListarPorConfirmarAsync(CajeraTemixco);
        Assert.True(Assert.Single(cola).Abonos[0].TieneComprobante);
        await _svc.VerAsync(ap.Id, ap.Abonos[0].Id, Pin, CajeraTemixco);
        var pagado = await _apartadoSvc.ConfirmarPagoAsync(ap.Id, ap.Abonos[0].Id, CajeraTemixco);

        // 3. La clienta lo ve pagado y ya no se puede subir otro comprobante.
        Assert.Equal("liquidado", pagado.Estado);
        Assert.Equal(600m, pagado.Pagado);
        Assert.Empty(await _apartadoSvc.ListarPorConfirmarAsync(CajeraTemixco));
        await Assert.ThrowsAsync<DomnaException>(() => _svc.FirmarSubidaAsync(ap.Id, "image/jpeg", "c1"));
    }

    [Fact]
    public async Task Un_pedido_en_revision_no_vence_aunque_pasen_los_dias()
    {
        var (ap, _) = await ConComprobante();
        _reloj.Ahora = _reloj.Ahora.AddDays(10);

        var visto = await _apartadoSvc.ObtenerAsync(ap.Id);
        Assert.Equal("activo", visto.Estado);
    }
}
