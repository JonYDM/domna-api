using Domna.Application.Dtos;
using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>Guardar stock en lote y mover entre sucursales, con historial y respeto a lo apartado.</summary>
public class StockServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly ProductoRepoFake _productos = new();
    private readonly ApartadoRepoFake _apartados = new();
    private readonly MovimientoRepoFake _movimientos = new();
    private readonly StockService _svc;
    private readonly ApartadoService _apartadoSvc;
    private readonly ProductoService _productoSvc;
    private readonly Producto _vestido;

    public StockServiceTests()
    {
        _svc = new StockService(_productos, _apartados, _movimientos, _reloj);
        var clientas = new ClientaRepoFake(
            Clienta.Crear("c1", "Ana", "Lopez", "7771234567", "ana@correo.com", OrigenClienta.Registro, _reloj.Ahora));
        _apartadoSvc = new ApartadoService(_apartados, _productos, clientas, new ConfigRepoFake(), _reloj);
        _productoSvc = new ProductoService(_productos, new CategoriaRepoFake(), _apartados, _reloj);

        var stock = new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
        {
            [SucursalId.Temixco] = new Dictionary<string, int[]> { ["M"] = new[] { 5 }, ["G"] = new[] { 3 } },
            [SucursalId.Azteca] = new Dictionary<string, int[]> { ["M"] = new[] { 2 }, ["G"] = new[] { 0 } },
        };
        var n = 0;
        _vestido = Producto.Crear(
            "p1", "Vestido Primavera", null, "vestidos", Silueta.Vestido, 600m, 250m, true,
            new[] { new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa) },
            new[] { "M", "G" }, stock, _reloj.Ahora,
            nuevoIdColor: () => "col1", nuevoIdVariante: () => $"v{++n}"); // v1 = M, v2 = G
        _productos.AgregarAsync(_vestido).GetAwaiter().GetResult();
    }

    private static CambioStockInput Cambio(string variante, string sucursal, int nuevo) => new(variante, sucursal, nuevo);
    private static GuardarStockInput Lote(string motivo, params CambioStockInput[] cambios) => new(cambios.ToList(), motivo);
    private int Stock(string varianteId, SucursalId s) => _vestido.Variantes.First(v => v.Id == varianteId).StockDe(s);

    private Task ApartarEnTemixco(int cantidad) => _apartadoSvc.CrearAsync("c1", new CrearApartadoInput(
        new List<LineaCarritoInput> { new("v1", cantidad) }, "sin_anticipo", "temixco", "online", "c1", "efectivo"));

    // ── Guardar en lote ──

    [Fact]
    public async Task Guarda_los_valores_absolutos_y_deja_un_renglon_por_cambio()
    {
        var res = await _svc.GuardarLoteAsync("p1", Lote("entrada",
            Cambio("v1", "temixco", 8), Cambio("v2", "azteca", 4)));

        Assert.Equal(2, res.Actualizados);
        Assert.Equal(8, Stock("v1", SucursalId.Temixco));
        Assert.Equal(4, Stock("v2", SucursalId.Azteca));
        Assert.Equal(2, _movimientos.Todos.Count);

        var m1 = _movimientos.Todos.First(m => m.VarianteId == "v1");
        Assert.Equal(3, m1.Delta); // 5 → 8
        Assert.Equal(MotivoAjuste.Entrada, m1.Motivo);
        Assert.Equal(SucursalId.Temixco, m1.Sucursal);
        Assert.Equal(_reloj.Ahora, m1.Fecha);
        Assert.Equal("p1", m1.ProductoId);
    }

    [Fact]
    public async Task Una_merma_registra_delta_negativo()
    {
        await _svc.GuardarLoteAsync("p1", Lote("merma", Cambio("v1", "temixco", 3)));

        var m = Assert.Single(_movimientos.Todos);
        Assert.Equal(-2, m.Delta);
        Assert.Equal(MotivoAjuste.Merma, m.Motivo);
    }

    [Fact]
    public async Task Un_cambio_sin_diferencia_no_deja_historial()
    {
        var res = await _svc.GuardarLoteAsync("p1", Lote("conteo",
            Cambio("v1", "temixco", 5), Cambio("v2", "temixco", 1)));

        Assert.Equal(2, res.Actualizados);
        Assert.Single(_movimientos.Todos);
        Assert.Equal("v2", _movimientos.Todos[0].VarianteId);
    }

    [Fact]
    public async Task Es_todo_o_nada_si_un_cambio_es_invalido_no_se_aplica_ninguno()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.GuardarLoteAsync("p1", Lote("conteo",
            Cambio("v1", "temixco", 9),
            Cambio("v2", "temixco", -1))));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(5, Stock("v1", SucursalId.Temixco)); // el primero tampoco se aplicó
        Assert.Empty(_movimientos.Todos);
    }

    [Fact]
    public async Task No_deja_el_stock_por_debajo_de_lo_apartado_y_no_aplica_nada()
    {
        await ApartarEnTemixco(3);

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.GuardarLoteAsync("p1", Lote("merma",
            Cambio("v2", "temixco", 1),
            Cambio("v1", "temixco", 2)))); // hay 3 apartadas en v1

        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("3", ex.Message);
        Assert.Equal(3, Stock("v2", SucursalId.Temixco));
        Assert.Equal(5, Stock("v1", SucursalId.Temixco));
        Assert.Empty(_movimientos.Todos);
    }

    [Fact]
    public async Task Puede_bajar_justo_hasta_lo_apartado()
    {
        await ApartarEnTemixco(3);
        await _svc.GuardarLoteAsync("p1", Lote("merma", Cambio("v1", "temixco", 3)));
        Assert.Equal(3, Stock("v1", SucursalId.Temixco));
    }

    [Fact]
    public async Task Lo_apartado_en_una_sucursal_no_bloquea_cambios_en_la_otra()
    {
        await ApartarEnTemixco(3);
        await _svc.GuardarLoteAsync("p1", Lote("merma", Cambio("v1", "azteca", 0)));
        Assert.Equal(0, Stock("v1", SucursalId.Azteca));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(-5)]
    public async Task Rechaza_cantidades_fuera_de_rango(int nuevo)
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.GuardarLoteAsync("p1", Lote("entrada", Cambio("v1", "temixco", nuevo))));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Acepta_el_maximo_de_999()
    {
        await _svc.GuardarLoteAsync("p1", Lote("entrada", Cambio("v1", "temixco", 999)));
        Assert.Equal(999, Stock("v1", SucursalId.Temixco));
    }

    [Fact]
    public async Task Rechaza_lote_vacio_producto_o_variante_inexistente_y_motivos_invalidos()
    {
        var vacio = await Assert.ThrowsAsync<DomnaException>(() => _svc.GuardarLoteAsync("p1", Lote("entrada")));
        Assert.Equal(400, vacio.StatusCode);

        var sinProducto = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.GuardarLoteAsync("nada", Lote("entrada", Cambio("v1", "temixco", 1))));
        Assert.Equal(404, sinProducto.StatusCode);

        var sinVariante = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.GuardarLoteAsync("p1", Lote("entrada", Cambio("zzz", "temixco", 1))));
        Assert.Equal(404, sinVariante.StatusCode);

        var motivo = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.GuardarLoteAsync("p1", Lote("regalo", Cambio("v1", "temixco", 1))));
        Assert.Equal(400, motivo.StatusCode);

        var sucursal = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.GuardarLoteAsync("p1", Lote("entrada", Cambio("v1", "narnia", 1))));
        Assert.Equal(400, sucursal.StatusCode);
    }

    [Fact]
    public async Task El_traspaso_no_se_guarda_por_lote()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _svc.GuardarLoteAsync("p1", Lote("traspaso", Cambio("v1", "temixco", 1))));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(5, Stock("v1", SucursalId.Temixco));
    }

    [Fact]
    public async Task Rechaza_dos_cambios_para_la_misma_talla_y_sucursal()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.GuardarLoteAsync("p1", Lote("conteo",
            Cambio("v1", "temixco", 9), Cambio("v1", "TEMIXCO", 1))));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(5, Stock("v1", SucursalId.Temixco));
    }

    [Fact]
    public async Task Reabastecer_una_talla_agotada_marca_la_fecha_de_reabasto()
    {
        Assert.Null(_vestido.ReabastecidoEl);
        await _svc.GuardarLoteAsync("p1", Lote("entrada", Cambio("v1", "temixco", 0), Cambio("v1", "azteca", 0)));
        _reloj.Ahora = _reloj.Ahora.AddDays(2);
        await _svc.GuardarLoteAsync("p1", Lote("entrada", Cambio("v1", "temixco", 4)));
        Assert.Equal(_reloj.Ahora, _vestido.ReabastecidoEl);
    }

    // ── Mover ──

    [Fact]
    public async Task Mover_traslada_piezas_y_deja_dos_renglones_de_traspaso()
    {
        await _svc.MoverAsync(new MoverStockInput("v1", "temixco", 2));

        Assert.Equal(3, Stock("v1", SucursalId.Temixco));
        Assert.Equal(4, Stock("v1", SucursalId.Azteca));
        Assert.Equal(2, _movimientos.Todos.Count);
        Assert.All(_movimientos.Todos, m => Assert.Equal(MotivoAjuste.Traspaso, m.Motivo));
        Assert.Equal(-2, _movimientos.Todos.Single(m => m.Sucursal == SucursalId.Temixco).Delta);
        Assert.Equal(2, _movimientos.Todos.Single(m => m.Sucursal == SucursalId.Azteca).Delta);
    }

    [Fact]
    public async Task Mover_hacia_temixco_desde_azteca_funciona_igual()
    {
        await _svc.MoverAsync(new MoverStockInput("v1", "azteca", 2));
        Assert.Equal(7, Stock("v1", SucursalId.Temixco));
        Assert.Equal(0, Stock("v1", SucursalId.Azteca));
    }

    [Fact]
    public async Task Mover_no_puede_llevarse_lo_apartado()
    {
        await ApartarEnTemixco(4); // 5 físicas, 4 apartadas → 1 libre

        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.MoverAsync(new MoverStockInput("v1", "temixco", 2)));
        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("Solo hay 1", ex.Message);
        Assert.Equal(5, Stock("v1", SucursalId.Temixco));
        Assert.Equal(2, Stock("v1", SucursalId.Azteca));
        Assert.Empty(_movimientos.Todos);

        await _svc.MoverAsync(new MoverStockInput("v1", "temixco", 1)); // la libre sí
        Assert.Equal(4, Stock("v1", SucursalId.Temixco));
    }

    [Fact]
    public async Task Mover_mas_de_lo_que_hay_responde_409()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.MoverAsync(new MoverStockInput("v1", "azteca", 3)));
        Assert.Equal(409, ex.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Mover_cantidad_invalida_responde_400(int cantidad)
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.MoverAsync(new MoverStockInput("v1", "temixco", cantidad)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Mover_variante_inexistente_responde_404()
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.MoverAsync(new MoverStockInput("zzz", "temixco", 1)));
        Assert.Equal(404, ex.StatusCode);
    }

    // ── Ajuste individual (POST productos/{id}/stock): misma protección ──

    [Fact]
    public async Task El_ajuste_individual_tambien_respeta_lo_apartado()
    {
        await ApartarEnTemixco(3);

        var ex = await Assert.ThrowsAsync<DomnaException>(() =>
            _productoSvc.AjustarStockAsync("p1", new AjusteStockInput("v1", "temixco", 2)));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(5, Stock("v1", SucursalId.Temixco));

        await _productoSvc.AjustarStockAsync("p1", new AjusteStockInput("v1", "temixco", 3));
        Assert.Equal(3, Stock("v1", SucursalId.Temixco));
    }
}
