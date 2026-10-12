namespace Domna.Domain;

/// <summary>
/// Apartado/venta (agregado raíz). "Compra" (modalidad) se liquida de inmediato: es una venta
/// directa, no reserva. Las otras dos modalidades reservan stock hasta vencer o liquidarse.
/// Las líneas llegan ya resueltas (precio/costo/sucursal congelados) desde el caso de uso, porque
/// resolverlas requiere consultar variantes de varios productos (fuera del agregado).
/// </summary>
public class Apartado
{
    private readonly List<LineaApartado> _lineas = new();
    private readonly List<Abono> _abonos = new();

    public string Id { get; private set; } = default!;
    public string Folio { get; private set; } = default!;
    public string ClientaId { get; private set; } = default!;
    public string ClientaNombre { get; private set; } = default!;
    public string ClientaTelefono { get; private set; } = default!;
    public decimal Subtotal { get; private set; }

    /// <summary>Penalización de un apartado vencido anterior, cobrada en este.</summary>
    public decimal Penalizacion { get; private set; }
    public decimal Total { get; private set; }
    public ModalidadApartado Modalidad { get; private set; }
    public Canal Canal { get; private set; }
    public SucursalId Entrega { get; private set; }
    public bool RequiereTraslado { get; private set; }
    public EstadoEntrega EstadoEntrega { get; private set; }
    public DateTime Creado { get; private set; }
    public DateTime VenceEl { get; private set; }
    public DateTime ListoEstimado { get; private set; }
    public EstadoApartado Estado { get; private set; }
    public DateTime? CerradoEl { get; private set; }

    /// <summary>Si ya se generó la penalización al vencer (para no duplicarla).</summary>
    public bool Penalizado { get; private set; }

    public IReadOnlyList<LineaApartado> Lineas => _lineas.AsReadOnly();
    public IReadOnlyList<Abono> Abonos => _abonos.AsReadOnly();

    public decimal Pagado => _abonos.Sum(a => a.Monto);
    public decimal Saldo => Math.Max(0, Total - Pagado);
    public int Piezas => _lineas.Sum(l => l.Cantidad);
    public decimal Ganancia => _lineas.Sum(l => l.Ganancia);

    private Apartado() { }

    /// <summary>
    /// Crea el apartado/venta ya cotizado. Si hay anticipo (compra = total, con anticipo = una
    /// parte), se registra como primer abono. Las compras se liquidan de inmediato (venta directa).
    /// </summary>
    public static Apartado Crear(
        string id,
        string folio,
        string clientaId,
        string clientaNombre,
        string clientaTelefono,
        List<LineaDraft> lineas,
        decimal subtotal,
        decimal penalizacion,
        decimal total,
        ModalidadApartado modalidad,
        Canal canal,
        SucursalId entrega,
        bool requiereTraslado,
        DateTime creado,
        DateTime venceEl,
        DateTime listoEstimado,
        decimal anticipo,
        MetodoPago metodoAnticipo,
        Func<string> nuevoIdLinea,
        Func<string> nuevoIdAbono)
    {
        if (lineas.Count == 0)
            throw DomnaException.BadRequest("Agrega al menos una prenda.");

        var compra = modalidad == ModalidadApartado.Compra;
        var a = new Apartado
        {
            Id = id,
            Folio = folio,
            ClientaId = clientaId,
            ClientaNombre = clientaNombre,
            ClientaTelefono = clientaTelefono,
            Subtotal = subtotal,
            Penalizacion = penalizacion,
            Total = total,
            Modalidad = modalidad,
            Canal = canal,
            Entrega = entrega,
            RequiereTraslado = requiereTraslado,
            EstadoEntrega = requiereTraslado ? EstadoEntrega.EnOrigen : EstadoEntrega.Listo,
            Creado = creado,
            VenceEl = venceEl,
            ListoEstimado = listoEstimado,
            Estado = EstadoApartado.Activo,
            Penalizado = false,
        };
        foreach (var l in lineas)
        {
            a._lineas.Add(new LineaApartado(
                nuevoIdLinea(), a.Id, l.VarianteId, l.ProductoId, l.Nombre, l.Silueta, l.Talla,
                l.ColorNombre, l.ColorHex, l.Precio, l.Costo, l.Cantidad, l.SucursalOrigen));
        }

        if (anticipo > 0)
        {
            var concepto = compra ? ConceptoAbono.Compra : ConceptoAbono.Anticipo;
            a._abonos.Add(new Abono(nuevoIdAbono(), a.Id, anticipo, creado, metodoAnticipo, concepto));
        }

        // Compra: pago completo → venta directa, se liquida de inmediato (descuenta stock físico).
        if (compra) a.Liquidar(creado);

        return a;
    }

    /// <summary>
    /// Registra un abono. Si cubre el saldo exacto, liquida (la reserva se convierte en venta).
    /// Un primer abono en "sin anticipo" la pasa a vigencia completa de "con anticipo".
    /// </summary>
    public void Abonar(Func<string> nuevoIdAbono, decimal monto, DateTime hoy, MetodoPago metodo, int vigenciaConAnticipoDias)
    {
        if (Estado != EstadoApartado.Activo)
            throw DomnaException.BadRequest("El apartado ya no está activo.");
        var saldo = Saldo;
        if (monto <= 0 || monto > saldo)
            throw DomnaException.BadRequest("Monto inválido.");

        var liquida = monto == saldo;
        var concepto = liquida ? ConceptoAbono.Liquidacion : ConceptoAbono.Abono;
        _abonos.Add(new Abono(nuevoIdAbono(), Id, monto, hoy, metodo, concepto));

        if (Modalidad == ModalidadApartado.SinAnticipo && !liquida)
        {
            Modalidad = ModalidadApartado.Anticipo;
            VenceEl = hoy.AddDays(vigenciaConAnticipoDias);
        }

        if (liquida) Liquidar(hoy);
    }

    /// <summary>Liquida: la reserva se vuelve venta (el stock físico se descuenta en el caso de uso).</summary>
    private void Liquidar(DateTime hoy)
    {
        Estado = EstadoApartado.Liquidado;
        CerradoEl = hoy;
    }

    /// <summary>Cancelar no borra: cambia el estado y libera la reserva de stock. El anticipo no se devuelve.</summary>
    public void Cancelar(DateTime hoy)
    {
        if (Estado != EstadoApartado.Activo)
            throw DomnaException.BadRequest("Solo se cancelan apartados activos.");
        Estado = EstadoApartado.Cancelado;
        CerradoEl = hoy;
    }

    /// <summary>Vence: libera la reserva de stock y marca la penalización (una sola vez) para la clienta.</summary>
    /// <returns>true si generó penalización nueva (para que el caso de uso la sume a la clienta).</returns>
    public bool Vencer(DateTime hoy)
    {
        if (Estado != EstadoApartado.Activo || VenceEl > hoy)
            return false;
        Estado = EstadoApartado.Vencido;
        CerradoEl = VenceEl;
        if (Penalizado) return false;
        Penalizado = true;
        return true;
    }

    public void AvanzarEntrega(EstadoEntrega siguiente)
    {
        if (Estado == EstadoApartado.Cancelado || Estado == EstadoApartado.Vencido)
            throw DomnaException.BadRequest("Ese apartado ya está cerrado.");
        if (siguiente == EstadoEntrega.Entregado && Estado != EstadoApartado.Liquidado)
            throw DomnaException.BadRequest("Se entrega cuando el apartado está liquidado.");
        EstadoEntrega = siguiente;
    }
}
