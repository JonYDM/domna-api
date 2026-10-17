using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Apartados y ventas de contado (modalidad "compra" = venta directa, canal físico es el POS).
/// Antes de cualquier lectura/escritura, procesa vencimientos (igual que el mock: "procesarVencimientos"
/// se evalúa en cada acceso, no por un job aparte). El stock reservado se calcula on-the-fly
/// consultando los apartados ACTIVOS (no se persiste un contador de reservas).
/// </summary>
public class ApartadoService : IApartadoService
{
    private const string PublicoGeneralId = "c-publico";

    private readonly IApartadoRepository _apartados;
    private readonly IProductoRepository _productos;
    private readonly IClientaRepository _clientas;
    private readonly IConfigRepository _config;
    private readonly IReloj _reloj;

    public ApartadoService(
        IApartadoRepository apartados,
        IProductoRepository productos,
        IClientaRepository clientas,
        IConfigRepository config,
        IReloj reloj)
    {
        _apartados = apartados;
        _productos = productos;
        _clientas = clientas;
        _config = config;
        _reloj = reloj;
    }

    // ── Lectura ──

    public async Task<List<ApartadoDto>> ListarAsync(string? estado, string? texto, string? tipo, CancellationToken ct = default) =>
        (await BuscarAsync(estado, texto, tipo, 1, int.MaxValue, ct)).Items;

    public const int TamanoPaginaMaximo = 50;

    public async Task<PaginaDto<ApartadoDto>> BuscarAsync(
        string? estado, string? texto, string? tipo, int pagina, int tamano, CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        pagina = Math.Max(1, pagina);
        // int.MaxValue lo usa la lista completa de siempre; las páginas de la app se acotan para no cargar de más.
        tamano = tamano == int.MaxValue ? tamano : Math.Clamp(tamano, 1, TamanoPaginaMaximo);

        var filtro = FiltroPedidos.Combinar(tipo, estado, texto, _reloj.Ahora);
        var resultado = await _apartados.BuscarAsync(filtro, porVencimiento: estado == "por_vencer", pagina, tamano, ct);
        var items = await MapearAsync(resultado.Items, ct);
        var hayMas = tamano != int.MaxValue && (long)pagina * tamano < resultado.Total;
        return new PaginaDto<ApartadoDto>(items, resultado.Total, pagina, tamano, hayMas);
    }

    public async Task<ConteosPedidosDto> ConteosAsync(CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        var ahora = _reloj.Ahora;
        var activos = await _apartados.ContarAsync(FiltroPedidos.Combinar("apartados", "activo", null, ahora), ct);
        var porEntregar = await _apartados.ContarAsync(FiltroPedidos.Combinar("compras", "por_entregar", null, ahora), ct);
        return new ConteosPedidosDto(activos, porEntregar);
    }

    public async Task<ApartadoDto> ObtenerAsync(string id, CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        var a = await _apartados.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Apartado no encontrado.");
        return await MapearAsync(a, ct);
    }

    public async Task<List<ApartadoDto>> ListarDeClientaAsync(string clientaId, CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        var lista = await _apartados.ListarDeClientaAsync(clientaId, ct);
        return await MapearAsync(lista, ct);
    }

    /// <summary>
    /// Convierte pedidos a DTO añadiendo la foto de cada prenda. Es UNA consulta por lote (no una por línea),
    /// y la foto se busca al momento, así que si la dueña cambia la foto de la prenda el pedido la refleja.
    /// </summary>
    private async Task<List<ApartadoDto>> MapearAsync(IEnumerable<Apartado> apartados, CancellationToken ct)
    {
        var lista = apartados.ToList();
        var ids = lista.SelectMany(a => a.Lineas).Select(l => l.ProductoId).Distinct().ToList();
        var prendas = (await _productos.ListarFotosAsync(ids, ct)).ToDictionary(p => p.Id);
        string? FotoDe(LineaApartado l) => prendas.TryGetValue(l.ProductoId, out var p) ? p.FotoPrincipal(l.ColorNombre) : null;
        return lista.Select(a => a.ToDto(FotoDe)).ToList();
    }

    private async Task<ApartadoDto> MapearAsync(Apartado apartado, CancellationToken ct) =>
        (await MapearAsync(new[] { apartado }, ct))[0];

    private static readonly TimeZoneInfo ZonaMx = ObtenerZonaMx();

    private static TimeZoneInfo ObtenerZonaMx()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)"); }
    }

    private static string DiaMx(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaMx);
        return local.ToString("yyyy-MM-dd");
    }

    public async Task<CorteDiaDto> CorteDiaAsync(string sucursal, CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        var suc = EnumMap.Sucursal(sucursal);
        var hoy = DiaMx(_reloj.Ahora);
        var lista = await _apartados.ListarAsync(ct);

        var delDia = lista.Where(a => a.Estado == EstadoApartado.Liquidado && a.CerradoEl is not null && DiaMx(a.CerradoEl.Value) == hoy);

        decimal ventas = 0, ganancia = 0;
        int piezas = 0, tickets = 0;
        foreach (var a in delDia)
        {
            var lineas = a.Lineas.Where(l => l.SucursalOrigen == suc).ToList();
            if (lineas.Count == 0) continue;
            ventas += lineas.Sum(l => l.Precio * l.Cantidad);
            ganancia += lineas.Sum(l => l.Ganancia);
            piezas += lineas.Sum(l => l.Cantidad);
            tickets++;
        }

        return new CorteDiaDto(sucursal, hoy, ventas, ganancia, piezas, tickets);
    }

    public async Task<MetricasDto> MetricasAsync(string? sucursal, CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        var hoy = _reloj.Ahora;
        var hoyMx = DiaMx(hoy);
        var suc = string.IsNullOrEmpty(sucursal) || sucursal == "todas" ? (SucursalId?)null : EnumMap.Sucursal(sucursal);

        var apartados = await _apartados.ListarAsync(ct);
        // Una compra en línea sin pagar aún no es un apartado: espera su confirmación en caja.
        var activos = apartados.Where(a => a.Estado == EstadoApartado.Activo && a.Modalidad != ModalidadApartado.Compra).ToList();
        var liquidados = apartados.Where(a => a.Estado == EstadoApartado.Liquidado).ToList();
        var delMes = liquidados.Where(a => a.CerradoEl is not null && MismoMesMx(a.CerradoEl.Value, hoy)).ToList();
        // Conversión de APARTADOS (las compras de contado no cuentan: siempre se concretan).
        var cerrados = apartados.Where(a => a.Estado != EstadoApartado.Activo && a.Modalidad != ModalidadApartado.Compra).ToList();
        var apartadosLiquidados = cerrados.Count(a => a.Estado == EstadoApartado.Liquidado);

        List<LineaApartado> LineasDeSucursal(Apartado a) =>
            suc is null ? a.Lineas.ToList() : a.Lineas.Where(l => l.SucursalOrigen == suc.Value).ToList();
        decimal TotalDeSucursal(Apartado a) => LineasDeSucursal(a).Sum(l => l.Precio * l.Cantidad);

        var config = await _config.ObtenerAsync(ct);

        // Stock bajo: productos activos con alguna variante disponible <= STOCK_BAJO.
        const int stockBajoUmbral = 2;
        var productos = await _productos.ListarAsync(incluirInactivos: false, ct);
        var varianteIds = productos.SelectMany(p => p.Variantes).Select(v => v.Id).ToList();
        var reservadoPorVariante = await _apartados.ObtenerReservadoPorVariantesAsync(varianteIds, ct);
        var stockBajo = new List<StockBajoItemDto>();
        foreach (var p in productos)
        {
            foreach (var v in p.Variantes)
            {
                var reservado = reservadoPorVariante.GetValueOrDefault(v.Id, new Dictionary<string, int>());
                var dispTemixco = Math.Max(0, v.StockDe(SucursalId.Temixco) - reservado.GetValueOrDefault("temixco"));
                var dispAzteca = Math.Max(0, v.StockDe(SucursalId.Azteca) - reservado.GetValueOrDefault("azteca"));
                var disponible = dispTemixco + dispAzteca;
                if (disponible > stockBajoUmbral) continue;
                var color = p.Colores.First(c => c.Id == v.ColorId);
                stockBajo.Add(new StockBajoItemDto(p.Id, p.Nombre, v.Talla, color.Nombre, color.Hex, disponible));
            }
        }
        stockBajo = stockBajo.OrderBy(x => x.Disponible).ToList();

        // Top productos por unidades vendidas/apartadas.
        var unidades = new Dictionary<string, TopProductoDto>();
        var colorDeTop = new Dictionary<string, string>();
        foreach (var a in liquidados.Concat(activos))
        {
            foreach (var l in LineasDeSucursal(a))
            {
                if (unidades.TryGetValue(l.ProductoId, out var existente))
                    unidades[l.ProductoId] = existente with { Unidades = existente.Unidades + l.Cantidad };
                else
                {
                    unidades[l.ProductoId] = new TopProductoDto(l.ProductoId, l.Nombre, EnumMap.Silueta(l.Silueta), l.ColorHex, l.Cantidad);
                    colorDeTop[l.ProductoId] = l.ColorNombre;
                }
            }
        }
        var topProductos = unidades.Values.OrderByDescending(x => x.Unidades).Take(5).ToList();
        var fotosTop = (await _productos.ListarFotosAsync(topProductos.Select(x => x.ProductoId).ToList(), ct)).ToDictionary(p => p.Id);
        topProductos = topProductos
            .Select(x => x with { ImagenUrl = fotosTop.TryGetValue(x.ProductoId, out var prenda) ? prenda.FotoPrincipal(colorDeTop.GetValueOrDefault(x.ProductoId)) : null })
            .ToList();

        // Ventas de los últimos 7 días (incluye hoy).
        var ventasUltimos7 = Enumerable.Range(-6, 7)
            .Select(offset =>
            {
                var dia = DiaMx(hoy.AddDays(offset));
                var total = liquidados
                    .Where(a => a.CerradoEl is not null && DiaMx(a.CerradoEl.Value) == dia)
                    .Sum(TotalDeSucursal);
                return new VentaDiaDto(dia, total);
            })
            .ToList();

        var penalizacionesMes = apartados.Count(a =>
            a.Estado == EstadoApartado.Vencido && a.Modalidad != ModalidadApartado.Compra &&
            a.CerradoEl is not null && MismoMesMx(a.CerradoEl.Value, hoy)) * config.Penalizacion;

        return new MetricasDto(
            activos.Count,
            activos.Sum(a => a.Total),
            activos.Sum(a => a.Saldo),
            activos.Count(PorVencer),
            delMes.Sum(TotalDeSucursal),
            delMes.Sum(a => LineasDeSucursal(a).Sum(l => l.Ganancia)),
            delMes.Sum(a => LineasDeSucursal(a).Sum(l => l.Cantidad)),
            cerrados.Count > 0 ? (int)Math.Round(apartadosLiquidados * 100.0 / cerrados.Count) : 0,
            apartados.Count(PorEntregar),
            delMes.Count(a => a.Modalidad == ModalidadApartado.Compra),
            penalizacionesMes,
            stockBajo,
            topProductos,
            ventasUltimos7);
    }

    private static bool MismoMesMx(DateTime a, DateTime b)
    {
        var pa = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(a, DateTimeKind.Utc), ZonaMx);
        var pb = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b, DateTimeKind.Utc), ZonaMx);
        return pa.Year == pb.Year && pa.Month == pb.Month;
    }

    private bool PorVencer(Apartado a) =>
        a.Estado == EstadoApartado.Activo && (a.VenceEl.Date - _reloj.Ahora.Date).TotalDays <= 3;

    private static bool PorEntregar(Apartado a) =>
        a.Estado == EstadoApartado.Liquidado && a.EstadoEntrega != EstadoEntrega.Entregado;

    // ── Cotizar ──

    public async Task<CotizacionApartadoDto> CotizarAsync(string? clientaId, CrearApartadoInput input, CancellationToken ct = default)
    {
        // Un apartado vencido aún sin procesar seguiría reservando stock: se procesa antes de calcular.
        await ProcesarVencimientosAsync(ct);
        var hoy = _reloj.Ahora;
        var penalizacionPendiente = 0m;
        if (!string.IsNullOrEmpty(clientaId))
        {
            var clienta = await _clientas.ObtenerPorIdAsync(clientaId, ct);
            penalizacionPendiente = clienta?.PenalizacionPendiente ?? 0;
        }

        List<LineaResuelta> lineas;
        try
        {
            lineas = await ResolverLineasAsync(input, ct);
        }
        catch (DomnaException e) when (e.StatusCode == 409)
        {
            // La cotización no revienta por falta de stock: lo informa con sucursalOrigen = null.
            return new CotizacionApartadoDto(0, penalizacionPendiente, penalizacionPendiente, 0, hoy, false, hoy, null);
        }

        var config = await _config.ObtenerAsync(ct);
        var entrega = EnumMap.Sucursal(input.Entrega);
        var modalidad = EnumMap.Modalidad(input.Modalidad);

        var subtotal = lineas.Sum(l => l.Precio * l.Cantidad);
        var total = subtotal + penalizacionPendiente;
        var compra = modalidad == ModalidadApartado.Compra;
        var conAnticipo = modalidad == ModalidadApartado.Anticipo;
        var vigenciaDias = VigenciaDias(config, modalidad, EnumMap.Canal(input.Canal));
        var requiereTraslado = lineas.Any(l => l.SucursalOrigen != entrega);
        var listoEstimado = requiereTraslado ? SumarDiasHabiles(hoy, config.DiasTraslado) : hoy;
        var anticipo = compra ? total : conAnticipo ? AnticipoDe(total, config.AnticipoPct) : 0;

        return new CotizacionApartadoDto(
            subtotal, penalizacionPendiente, total, anticipo,
            hoy.AddDays(vigenciaDias), requiereTraslado, listoEstimado,
            EnumMap.Sucursal(lineas[0].SucursalOrigen));
    }

    private static decimal AnticipoDe(decimal total, int pct) => Math.Ceiling(total * pct / 100m);

    /// <summary>
    /// Días de vigencia desde hoy. En línea el pago nace por confirmar, así que todos los pedidos
    /// tienen la misma ventana corta para pagar (la vigencia completa de un anticipo empieza cuando
    /// la tienda lo confirma). En mostrador la compra se cobra en el momento.
    /// </summary>
    private static int VigenciaDias(ConfigBoutique config, ModalidadApartado modalidad, Canal canal)
    {
        if (canal == Canal.Online) return config.VigenciaSinAnticipoDias;
        return modalidad switch
        {
            ModalidadApartado.Compra => 0,
            ModalidadApartado.Anticipo => config.VigenciaConAnticipoDias,
            _ => config.VigenciaSinAnticipoDias,
        };
    }

    private static DateTime SumarDiasHabiles(DateTime d, int n)
    {
        var r = d;
        var restantes = n;
        while (restantes > 0)
        {
            r = r.AddDays(1);
            if (r.DayOfWeek != DayOfWeek.Sunday) restantes--;
        }
        return r;
    }

    // ── Crear (apartar o comprar) ──

    public async Task<ApartadoDto> CrearAsync(string? clientaId, CrearApartadoInput input, CancellationToken ct = default)
    {
        // Libera reservas y cuenta penalizaciones de apartados vencidos antes de validar límite y stock.
        await ProcesarVencimientosAsync(ct);
        var hoy = _reloj.Ahora;
        var config = await _config.ObtenerAsync(ct);
        if (config.Suspendida)
            throw new DomnaException(423, "La tienda está en pausa por el momento.");

        var destino = string.IsNullOrEmpty(clientaId) ? PublicoGeneralId : clientaId;
        var clienta = destino == PublicoGeneralId
            ? null
            : await _clientas.ObtenerPorIdAsync(destino, ct) ?? throw DomnaException.NotFound("Clienta no encontrada.");

        var modalidad = EnumMap.Modalidad(input.Modalidad);
        var compra = modalidad == ModalidadApartado.Compra;
        var lineas = await ResolverLineasAsync(input, ct);

        // Para apartados (no compra), todas las prendas deben permitir apartado.
        if (!compra)
        {
            var productosUsados = await ObtenerProductosDeLineasAsync(lineas, ct);
            var noApartable = lineas.FirstOrDefault(l => !productosUsados[l.ProductoId].PermiteApartado);
            if (noApartable is not null)
                throw DomnaException.BadRequest($"{noApartable.Nombre} solo se vende de contado.");

            // Límite de apartados activos (no aplica a compras: no reservan).
            if (!string.IsNullOrEmpty(clientaId))
            {
                var activos = await _apartados.ContarActivosDeClientaAsync(clientaId, ct);
                if (activos >= config.LimiteApartadosActivos)
                    throw DomnaException.BadRequest(
                        $"Ya tienes {activos} apartados activos. Liquida uno para apartar otra prenda.");
            }
        }

        var cot = CotizarInterno(clienta, lineas, config, input, hoy);
        if (cot.SucursalOrigen is null)
            throw DomnaException.Conflict("Ya no hay stock de una de las prendas. Revisa el carrito.");

        var folio = await SiguienteFolioAsync(ct);
        var entrega = EnumMap.Sucursal(input.Entrega);
        var canal = EnumMap.Canal(input.Canal);
        var metodoAnticipo = EnumMap.Metodo(input.MetodoAnticipo);
        var clientaNombre = clienta is null ? "Público general" : $"{clienta.Nombre} {clienta.Apellidos}".Trim();
        var clientaTelefono = clienta?.Telefono ?? "";

        var drafts = lineas.Select(l => new LineaDraft(
            l.VarianteId, l.ProductoId, l.Nombre, l.Silueta, l.Talla, l.ColorNombre, l.ColorHex,
            l.Precio, l.Costo, l.Cantidad, l.SucursalOrigen)).ToList();

        var apartado = Apartado.Crear(
            NuevoId("ap"), folio, destino, clientaNombre, clientaTelefono,
            drafts, cot.Subtotal, cot.Penalizacion, cot.Total, modalidad, canal, entrega,
            cot.RequiereTraslado, hoy, cot.VenceEl, cot.ListoEstimado, cot.Anticipo, metodoAnticipo,
            () => NuevoId("li"), () => NuevoId("ab"));

        // Si quedó liquidado de inmediato (compra), descuenta stock físico real.
        if (apartado.Estado == EstadoApartado.Liquidado)
            await DescontarStockFisicoAsync(apartado, ct);

        await _apartados.AgregarAsync(apartado, ct);

        // La penalización pendiente se cobra en esta compra o apartado.
        if (clienta is not null && clienta.PenalizacionPendiente > 0)
            clienta.AjustarPenalizacion(-clienta.PenalizacionPendiente);

        // Dos pedidos simultáneos pueden tomar el mismo folio y chocar con el índice único al guardar.
        // Reintentamos hasta 3 veces reasignando el folio al mayor existente + 1 (no reutiliza números).
        var intento = 0;
        while (true)
        {
            try
            {
                await _apartados.GuardarAsync(ct);
                break;
            }
            catch (Exception ex) when (_apartados.EsColisionDeFolio(ex) && intento < MaxReintentosFolio)
            {
                intento++;
                var siguiente = await _apartados.ObtenerMaxFolioAsync(ct) + 1;
                apartado.ReasignarFolio(FormatearFolio(siguiente));
            }
        }
        await _productos.GuardarAsync(ct);
        if (clienta is not null) await _clientas.GuardarAsync(ct);

        return await MapearAsync(apartado, ct);
    }

    // ── Abonar ──

    public async Task<ApartadoDto> AbonarAsync(RegistrarAbonoInput input, CancellationToken ct = default)
    {
        await ProcesarVencimientosAsync(ct);
        var a = await _apartados.ObtenerPorIdAsync(input.ApartadoId, ct)
            ?? throw DomnaException.NotFound("Apartado no encontrado.");
        var config = await _config.ObtenerAsync(ct);

        a.Abonar(() => NuevoId("ab"), input.Monto, _reloj.Ahora, EnumMap.Metodo(input.Metodo), config.VigenciaConAnticipoDias);

        if (a.Estado == EstadoApartado.Liquidado)
        {
            await DescontarStockFisicoAsync(a, ct);
            await _productos.GuardarAsync(ct);
        }

        await _apartados.GuardarAsync(ct);
        return await MapearAsync(a, ct);
    }

    // ── Cancelar / avanzar entrega ──

    public async Task<ApartadoDto> CancelarAsync(string id, CancellationToken ct = default)
    {
        var a = await _apartados.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Apartado no encontrado.");
        a.Cancelar(_reloj.Ahora);
        await _apartados.GuardarAsync(ct);
        return await MapearAsync(a, ct);
    }

    public async Task<ApartadoDto> AvanzarEntregaAsync(string id, string siguiente, CancellationToken ct = default)
    {
        var a = await _apartados.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Apartado no encontrado.");
        a.AvanzarEntrega(EnumMap.EstadoEntrega(siguiente));
        await _apartados.GuardarAsync(ct);
        return await MapearAsync(a, ct);
    }

    // ── Pagos por confirmar (pedidos en línea) ──

    public async Task<List<ApartadoDto>> ListarPorConfirmarAsync(string usuarioId, CancellationToken ct = default)
    {
        var staff = await ObtenerStaffAsync(usuarioId, ct);
        await ProcesarVencimientosAsync(ct);
        var lista = await _apartados.ListarAsync(ct);
        var porConfirmar = lista
            .Where(a => a.Estado == EstadoApartado.Activo && a.TienePagoPendiente)
            .Where(a => PuedeVerSucursal(staff, a))
            .OrderBy(a => a.Creado)
            .ToList();
        return await MapearAsync(porConfirmar, ct);
    }

    public async Task<ApartadoDto> ConfirmarPagoAsync(string apartadoId, string abonoId, string usuarioId, CancellationToken ct = default)
    {
        var staff = await ObtenerStaffAsync(usuarioId, ct);
        await ProcesarVencimientosAsync(ct);
        var a = await ObtenerParaRevisarAsync(apartadoId, staff, ct);
        var config = await _config.ObtenerAsync(ct);

        var cambio = a.ConfirmarPago(abonoId, NombreStaff(staff), _reloj.Ahora, config.VigenciaConAnticipoDias);

        // Solo al confirmar por primera vez (y si quedó liquidado) se descuenta el stock físico.
        // Un reintento tras perder señal no vuelve a descontarlo.
        if (cambio && a.Estado == EstadoApartado.Liquidado)
        {
            await DescontarStockFisicoAsync(a, ct);
            await _productos.GuardarAsync(ct);
        }
        await _apartados.GuardarAsync(ct);
        return await MapearAsync(a, ct);
    }

    public async Task<ApartadoDto> RechazarPagoAsync(string apartadoId, string abonoId, string motivo, string usuarioId, CancellationToken ct = default)
    {
        var staff = await ObtenerStaffAsync(usuarioId, ct);
        await ProcesarVencimientosAsync(ct);
        var a = await ObtenerParaRevisarAsync(apartadoId, staff, ct);

        a.RechazarPago(abonoId, NombreStaff(staff), _reloj.Ahora, motivo);

        await _apartados.GuardarAsync(ct);
        return await MapearAsync(a, ct);
    }

    private async Task<Apartado> ObtenerParaRevisarAsync(string apartadoId, Clienta staff, CancellationToken ct)
    {
        var a = await _apartados.ObtenerPorIdAsync(apartadoId, ct)
            ?? throw DomnaException.NotFound("Pedido no encontrado.");
        if (!PuedeVerSucursal(staff, a))
            throw new DomnaException(403, "Ese pedido es de otra sucursal.");
        return a;
    }

    /// <summary>La dueña ve todas las sucursales; la cajera solo los pedidos que se entregan en la suya.</summary>
    private static bool PuedeVerSucursal(Clienta staff, Apartado a) =>
        staff.Rol == Rol.Duena || (staff.SucursalCajera is not null && staff.SucursalCajera == a.Entrega);

    private async Task<Clienta> ObtenerStaffAsync(string usuarioId, CancellationToken ct)
    {
        var u = await _clientas.ObtenerPorIdAsync(usuarioId, ct);
        if (u is null || !u.Activo || (u.Rol != Rol.Duena && u.Rol != Rol.Cajera))
            throw new DomnaException(403, "No tienes permiso para esto.");
        return u;
    }

    private static string NombreStaff(Clienta staff) => $"{staff.Nombre} {staff.Apellidos}".Trim();

    // ── Vencimientos (se evalúan en cada lectura, igual que el mock) ──

    private async Task ProcesarVencimientosAsync(CancellationToken ct)
    {
        var hoy = _reloj.Ahora;
        var vencidos = await _apartados.ListarActivosVencidosAsync(hoy, ct);
        if (vencidos.Count == 0) return;

        var config = await _config.ObtenerAsync(ct);
        var cambio = false;
        foreach (var a in vencidos)
        {
            if (!a.Vencer(hoy)) continue;
            cambio = true;
            if (a.ClientaId == PublicoGeneralId) continue;
            var clienta = await _clientas.ObtenerPorIdAsync(a.ClientaId, ct);
            clienta?.AjustarPenalizacion(config.Penalizacion);
        }
        if (cambio)
        {
            await _apartados.GuardarAsync(ct);
            await _clientas.GuardarAsync(ct);
        }
    }

    // ── Resolución de líneas del carrito (multi-producto) ──

    private record LineaResuelta(
        string VarianteId, string ProductoId, string Nombre, Silueta Silueta, string Talla,
        string ColorNombre, string ColorHex, decimal Precio, decimal Costo, int Cantidad, SucursalId SucursalOrigen);

    /// <summary>Resuelve cada línea: valida stock disponible real y congela precio/costo/sucursal.</summary>
    private async Task<List<LineaResuelta>> ResolverLineasAsync(CrearApartadoInput input, CancellationToken ct)
    {
        if (input.Lineas is null || input.Lineas.Count == 0)
            throw DomnaException.BadRequest("Agrega al menos una prenda.");
        foreach (var l in input.Lineas)
            if (l.Cantidad < 1) throw DomnaException.BadRequest("Cantidad inválida.");

        var entrega = EnumMap.Sucursal(input.Entrega);
        var otra = entrega == SucursalId.Temixco ? SucursalId.Azteca : SucursalId.Temixco;

        // Suma de cantidades por variante en el MISMO carrito, para validar stock total pedido.
        var pedidoPorVariante = input.Lineas
            .GroupBy(l => l.VarianteId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

        var varianteIds = pedidoPorVariante.Keys.ToList();
        var reservadoPorVariante = await _apartados.ObtenerReservadoPorVariantesAsync(varianteIds, ct);

        var resueltas = new List<LineaResuelta>();
        foreach (var l in input.Lineas)
        {
            var enc = await BuscarProductoYVarianteAsync(l.VarianteId, ct);
            if (enc is null || !enc.Value.Producto.Activo)
                throw DomnaException.NotFound("Una de las prendas ya no está disponible.");
            var (producto, variante) = enc.Value;

            var reservado = reservadoPorVariante.GetValueOrDefault(variante.Id, new Dictionary<string, int>());
            var dispTemixco = Math.Max(0, variante.StockDe(SucursalId.Temixco) - reservado.GetValueOrDefault("temixco"));
            var dispAzteca = Math.Max(0, variante.StockDe(SucursalId.Azteca) - reservado.GetValueOrDefault("azteca"));

            var pedidoTotal = pedidoPorVariante[l.VarianteId];
            SucursalId? origen = null;
            var dispEntrega = entrega == SucursalId.Temixco ? dispTemixco : dispAzteca;
            var dispOtra = otra == SucursalId.Temixco ? dispTemixco : dispAzteca;
            if (dispEntrega >= pedidoTotal) origen = entrega;
            else if (dispOtra >= pedidoTotal) origen = otra;
            if (origen is null)
                throw DomnaException.Conflict($"Ya no hay stock suficiente de {producto.Nombre} ({variante.Talla}).");

            var color = producto.Colores.First(c => c.Id == variante.ColorId);
            resueltas.Add(new LineaResuelta(
                variante.Id, producto.Id, producto.Nombre, producto.Silueta, variante.Talla,
                color.Nombre, color.Hex, producto.Precio, producto.Costo, l.Cantidad, origen.Value));
        }
        return resueltas;
    }

    private async Task<(Producto Producto, Variante Variante)?> BuscarProductoYVarianteAsync(string varianteId, CancellationToken ct)
    {
        // No hay índice directo variante->producto en el repo; se resuelve por búsqueda de código no
        // aplica aquí. En su lugar, listamos (el catálogo de una boutique es pequeño) y buscamos.
        var todos = await _productos.ListarAsync(incluirInactivos: true, ct);
        foreach (var p in todos)
        {
            var v = p.Variantes.FirstOrDefault(x => x.Id == varianteId);
            if (v is not null) return (p, v);
        }
        return null;
    }

    private async Task<Dictionary<string, Producto>> ObtenerProductosDeLineasAsync(List<LineaResuelta> lineas, CancellationToken ct)
    {
        var resultado = new Dictionary<string, Producto>();
        foreach (var productoId in lineas.Select(l => l.ProductoId).Distinct())
        {
            var p = await _productos.ObtenerPorIdAsync(productoId, ct);
            if (p is not null) resultado[productoId] = p;
        }
        return resultado;
    }

    private static CotizacionInterna CotizarInterno(
        Clienta? clienta, List<LineaResuelta> lineas, ConfigBoutique config, CrearApartadoInput input, DateTime hoy)
    {
        var entrega = EnumMap.Sucursal(input.Entrega);
        var modalidad = EnumMap.Modalidad(input.Modalidad);
        var penalizacionPendiente = clienta?.PenalizacionPendiente ?? 0;
        var subtotal = lineas.Sum(l => l.Precio * l.Cantidad);
        var total = subtotal + penalizacionPendiente;
        var compra = modalidad == ModalidadApartado.Compra;
        var conAnticipo = modalidad == ModalidadApartado.Anticipo;
        var vigenciaDias = VigenciaDias(config, modalidad, EnumMap.Canal(input.Canal));
        var requiereTraslado = lineas.Any(l => l.SucursalOrigen != entrega);
        var listoEstimado = requiereTraslado ? SumarDiasHabiles(hoy, config.DiasTraslado) : hoy;
        var anticipo = compra ? total : conAnticipo ? AnticipoDe(total, config.AnticipoPct) : 0;

        return new CotizacionInterna(subtotal, penalizacionPendiente, total, anticipo,
            hoy.AddDays(vigenciaDias), requiereTraslado, listoEstimado, lineas[0].SucursalOrigen);
    }

    private record CotizacionInterna(
        decimal Subtotal, decimal Penalizacion, decimal Total, decimal Anticipo,
        DateTime VenceEl, bool RequiereTraslado, DateTime ListoEstimado, SucursalId? SucursalOrigen);

    /// <summary>Descuenta el stock físico real de cada línea (al liquidar: la reserva se vuelve venta).</summary>
    private async Task DescontarStockFisicoAsync(Apartado a, CancellationToken ct)
    {
        var hoy = _reloj.Ahora;
        foreach (var grupo in a.Lineas.GroupBy(l => l.ProductoId))
        {
            var producto = await _productos.ObtenerPorIdAsync(grupo.Key, ct);
            if (producto is null) continue;
            foreach (var l in grupo)
            {
                var variante = producto.Variantes.FirstOrDefault(v => v.Id == l.VarianteId);
                if (variante is null) continue;
                var nuevo = variante.StockDe(l.SucursalOrigen) - l.Cantidad;
                producto.AjustarStock(variante.Id, l.SucursalOrigen, Math.Max(0, nuevo), hoy);
            }
        }
    }

    /// <summary>
    /// Siguiente folio "DOM-NNNN" a partir del MAYOR existente + 1 (no por conteo: así no reutiliza
    /// números si hubiera huecos). Ante dos altas simultáneas el índice único puede chocar igual;
    /// de eso se encarga el reintento en <see cref="CrearAsync"/>.
    /// </summary>
    private async Task<string> SiguienteFolioAsync(CancellationToken ct)
    {
        var max = await _apartados.ObtenerMaxFolioAsync(ct);
        return FormatearFolio(max + 1);
    }

    private static string FormatearFolio(int numero) => $"DOM-{numero:D4}";

    private const int MaxReintentosFolio = 3;

    private static string NuevoId(string prefijo) => $"{prefijo}-{Guid.NewGuid():N}"[..14];
}
