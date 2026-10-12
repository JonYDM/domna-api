namespace Domna.Domain;

/// <summary>
/// Línea congelada del carrito al apartar/comprar: precio y costo quedan fijos aunque la prenda
/// cambie de precio después (para la ganancia real y lo que ya se le cobró a la clienta).
/// </summary>
public class LineaApartado
{
    public string Id { get; private set; } = default!;
    public string ApartadoId { get; private set; } = default!;
    public string VarianteId { get; private set; } = default!;
    public string ProductoId { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public Silueta Silueta { get; private set; }
    public string Talla { get; private set; } = default!;
    public string ColorNombre { get; private set; } = default!;
    public string ColorHex { get; private set; } = default!;
    public decimal Precio { get; private set; }
    public decimal Costo { get; private set; }
    public int Cantidad { get; private set; }
    public SucursalId SucursalOrigen { get; private set; }

    private LineaApartado() { }

    public LineaApartado(
        string id,
        string apartadoId,
        string varianteId,
        string productoId,
        string nombre,
        Silueta silueta,
        string talla,
        string colorNombre,
        string colorHex,
        decimal precio,
        decimal costo,
        int cantidad,
        SucursalId sucursalOrigen)
    {
        Id = id;
        ApartadoId = apartadoId;
        VarianteId = varianteId;
        ProductoId = productoId;
        Nombre = nombre;
        Silueta = silueta;
        Talla = talla;
        ColorNombre = colorNombre;
        ColorHex = colorHex;
        Precio = precio;
        Costo = costo;
        Cantidad = cantidad;
        SucursalOrigen = sucursalOrigen;
    }

    /// <summary>Ganancia de esta línea: (precio − costo) × cantidad.</summary>
    public decimal Ganancia => (Precio - Costo) * Cantidad;
}

/// <summary>
/// Datos crudos de una línea ya resuelta (precio/costo/sucursal congelados), antes de que el
/// agregado Apartado le asigne id y lo ligue a sí mismo. Lo construye el caso de uso.
/// </summary>
public record LineaDraft(
    string VarianteId,
    string ProductoId,
    string Nombre,
    Silueta Silueta,
    string Talla,
    string ColorNombre,
    string ColorHex,
    decimal Precio,
    decimal Costo,
    int Cantidad,
    SucursalId SucursalOrigen);

/// <summary>Un pago contra el apartado (anticipo, abono, liquidación o pago completo de compra).</summary>
public class Abono
{
    public string Id { get; private set; } = default!;
    public string ApartadoId { get; private set; } = default!;
    public decimal Monto { get; private set; }
    public DateTime Fecha { get; private set; }
    public MetodoPago Metodo { get; private set; }
    public ConceptoAbono Concepto { get; private set; }

    private Abono() { }

    public Abono(string id, string apartadoId, decimal monto, DateTime fecha, MetodoPago metodo, ConceptoAbono concepto)
    {
        if (monto <= 0)
            throw DomnaException.BadRequest("El monto debe ser mayor a cero.");
        Id = id;
        ApartadoId = apartadoId;
        Monto = monto;
        Fecha = fecha;
        Metodo = metodo;
        Concepto = concepto;
    }
}
