namespace Domna.Application.Dtos;

/// <summary>Una línea del carrito al cotizar/apartar/comprar.</summary>
public record LineaCarritoInput(string VarianteId, int Cantidad);

/// <summary>Entrada para cotizar o crear un apartado/compra.</summary>
public record CrearApartadoInput(
    List<LineaCarritoInput> Lineas,
    string Modalidad,
    string Entrega,
    string? Canal,
    string? ClientaId,
    string? MetodoAnticipo);

/// <summary>Lo que se ve antes de confirmar (no reserva nada todavía).</summary>
public record CotizacionApartadoDto(
    decimal Subtotal,
    decimal Penalizacion,
    decimal Total,
    decimal Anticipo,
    DateTime VenceEl,
    bool RequiereTraslado,
    DateTime ListoEstimado,
    string? SucursalOrigen);

public record LineaApartadoDto(
    string VarianteId,
    string ProductoId,
    string Nombre,
    string Silueta,
    string Talla,
    string ColorNombre,
    string ColorHex,
    decimal Precio,
    decimal Costo,
    int Cantidad,
    string SucursalOrigen);

public record AbonoDto(string Id, decimal Monto, DateTime Fecha, string Metodo, string Concepto);

public record ApartadoDto(
    string Id,
    string Folio,
    string ClientaId,
    string ClientaNombre,
    string ClientaTelefono,
    List<LineaApartadoDto> Lineas,
    decimal Subtotal,
    decimal Penalizacion,
    decimal Total,
    string Modalidad,
    string Canal,
    string Entrega,
    bool RequiereTraslado,
    string EstadoEntrega,
    List<AbonoDto> Abonos,
    decimal Pagado,
    decimal Saldo,
    DateTime Creado,
    DateTime VenceEl,
    DateTime ListoEstimado,
    string Estado,
    DateTime? CerradoEl);

public record RegistrarAbonoInput(string ApartadoId, decimal Monto, string Metodo);

public record AvanzarEntregaInput(string Siguiente);
