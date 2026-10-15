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

    /// <summary>Solo cuentan los pagos confirmados por la tienda; lo que la clienta declara en línea espera revisión.</summary>
    public decimal Pagado => _abonos.Where(a => a.Estado == EstadoPago.Confirmado).Sum(a => a.Monto);

    /// <summary>Pagos declarados que la tienda aún no confirma ni rechaza.</summary>
    public decimal PorConfirmar => _abonos.Where(a => a.Estado == EstadoPago.Pendiente).Sum(a => a.Monto);
    public bool TienePagoPendiente => _abonos.Any(a => a.Estado == EstadoPago.Pendiente);
    public decimal Saldo => Math.Max(0, Total - Pagado);
    public int Piezas => _lineas.Sum(l => l.Cantidad);
    public decimal Ganancia => _lineas.Sum(l => l.Ganancia);

    private Apartado() { }

    /// <summary>
    /// Reasigna el folio al reintentar un alta que chocó con el índice único (dos pedidos a la vez).
    /// Solo lo usa el caso de uso de creación, antes de que el apartado se persista con éxito.
    /// </summary>
    public void ReasignarFolio(string folio) => Folio = folio;

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
            // En línea la clienta solo declara el pago: espera confirmación. En mostrador cobra la cajera.
            var estadoPago = canal == Canal.Online ? EstadoPago.Pendiente : EstadoPago.Confirmado;
            a._abonos.Add(new Abono(nuevoIdAbono(), a.Id, anticipo, creado, metodoAnticipo, concepto, estadoPago));
        }

        // Compra en mostrador: pago completo cobrado → venta directa, se liquida de inmediato.
        // Compra en línea: queda activa (reservada) hasta que la tienda confirme el pago.
        if (compra && !a.TienePagoPendiente) a.Liquidar(creado);

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
        if (TienePagoPendiente)
            throw DomnaException.BadRequest("Tiene un pago por confirmar. Confírmalo o recházalo antes de registrar otro.");
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

    /// <summary>
    /// ¿Puede la clienta subir (o volver a subir) un comprobante? Mientras el pedido siga activo y
    /// haya una transferencia declarada esperando revisión, o una que la tienda rechazó.
    /// </summary>
    public bool AceptaComprobante =>
        Estado == EstadoApartado.Activo &&
        (_abonos.Any(a => a.Estado == EstadoPago.Pendiente && a.Metodo == MetodoPago.Transferencia) ||
         (!TienePagoPendiente && _abonos.Any(a => a.Estado == EstadoPago.Rechazado && a.Metodo == MetodoPago.Transferencia)));

    /// <summary>
    /// La clienta adjunta el comprobante de su transferencia. Si la tienda había rechazado el pago y
    /// no hay otro pendiente, abre un pago nuevo por el mismo monto para que pueda volver a intentarlo.
    /// </summary>
    public void AdjuntarComprobante(string clave, Func<string> nuevoIdAbono, DateTime hoy)
    {
        if (Estado != EstadoApartado.Activo)
            throw DomnaException.BadRequest("El pedido ya no está activo.");

        var abono = _abonos.LastOrDefault(a => a.Estado == EstadoPago.Pendiente && a.Metodo == MetodoPago.Transferencia);
        if (abono is null)
        {
            if (TienePagoPendiente)
                throw DomnaException.BadRequest("Tu pago por otro método está por confirmar en tienda.");
            var rechazado = _abonos.LastOrDefault(a => a.Estado == EstadoPago.Rechazado && a.Metodo == MetodoPago.Transferencia)
                ?? throw DomnaException.BadRequest("Este pedido no espera un comprobante de transferencia.");
            var monto = Math.Min(rechazado.Monto, Saldo);
            if (monto <= 0) throw DomnaException.BadRequest("Este pedido ya está pagado.");
            abono = new Abono(nuevoIdAbono(), Id, monto, hoy, MetodoPago.Transferencia, rechazado.Concepto, EstadoPago.Pendiente);
            _abonos.Add(abono);
        }
        abono.AdjuntarComprobante(clave);
    }

    /// <summary>
    /// La tienda aún revisa un comprobante: mientras tanto el pedido no vence, para no perder a la
    /// clienta por la demora de quien lo revisa.
    /// </summary>
    public bool EnRevision => _abonos.Any(a => a.Estado == EstadoPago.Pendiente && a.TieneComprobante);

    /// <summary>
    /// La tienda verificó un pago declarado en línea: pasa a contar como pagado. Si cubre todo el
    /// saldo, liquida (la reserva se vuelve venta). Si es el anticipo de un apartado, empieza la
    /// vigencia completa de "con anticipo".
    /// </summary>
    /// <summary>
    /// La tienda verificó un pago declarado en línea: pasa a contar como pagado. Si cubre todo el
    /// saldo, liquida (la reserva se vuelve venta). Si es el anticipo de un apartado, empieza la
    /// vigencia completa de "con anticipo".
    /// Confirmar un pago YA confirmado no vuelve a ejecutar nada (reintento de la cajera tras perder
    /// señal): devuelve el estado actual sin tocar stock ni quién lo revisó.
    /// </summary>
    /// <returns>true si se confirmó por primera vez; false si ya estaba confirmado (reintento).</returns>
    public bool ConfirmarPago(string abonoId, string por, DateTime hoy, int vigenciaConAnticipoDias)
    {
        var abono = _abonos.FirstOrDefault(a => a.Id == abonoId)
            ?? throw DomnaException.NotFound("Pago no encontrado.");
        // Reintento idempotente: ya estaba confirmado, no se repite nada.
        if (abono.Estado == EstadoPago.Confirmado) return false;

        if (Estado != EstadoApartado.Activo)
            throw DomnaException.BadRequest("El apartado ya no está activo; el pedido regresó a la tienda.");
        abono.Confirmar(por, hoy);

        if (Saldo == 0)
        {
            Liquidar(hoy);
            return true;
        }
        if (Modalidad == ModalidadApartado.SinAnticipo)
            Modalidad = ModalidadApartado.Anticipo;
        var nuevaVigencia = hoy.AddDays(vigenciaConAnticipoDias);
        if (nuevaVigencia > VenceEl) VenceEl = nuevaVigencia;
        return true;
    }

    /// <summary>
    /// La tienda no encontró el pago. El pedido sigue activo hasta que venza; la clienta puede volver
    /// a intentarlo. Rechazar un pago YA rechazado no repite nada: conserva el motivo original.
    /// </summary>
    public void RechazarPago(string abonoId, string por, DateTime hoy, string motivo)
    {
        var abono = _abonos.FirstOrDefault(a => a.Id == abonoId)
            ?? throw DomnaException.NotFound("Pago no encontrado.");
        // Reintento idempotente: ya estaba rechazado, se conserva el motivo original.
        if (abono.Estado == EstadoPago.Rechazado) return;

        if (Estado != EstadoApartado.Activo)
            throw DomnaException.BadRequest("El apartado ya no está activo.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw DomnaException.BadRequest("Escribe el motivo del rechazo.");
        abono.Rechazar(por, hoy, motivo.Trim());
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
        if (EnRevision) return false;
        Estado = EstadoApartado.Vencido;
        CerradoEl = VenceEl;
        // Una compra en línea sin pagar no es un apartado: libera la reserva sin cobrar penalización.
        if (Modalidad == ModalidadApartado.Compra) return false;
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
