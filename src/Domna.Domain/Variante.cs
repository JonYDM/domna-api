namespace Domna.Domain;

/// <summary>
/// Variante física de un producto = talla × color. Cada combinación es un SKU distinto y puede
/// tener su propio código de barras (EAN/UPC/Code128). El stock se guarda por sucursal.
/// Alineada con la interfaz Variante de src/types/api.ts.
/// </summary>
public class Variante
{
    public string Id { get; private set; } = default!;
    public string ProductoId { get; private set; } = default!;
    public string Talla { get; private set; } = default!;
    public string ColorId { get; private set; } = default!;
    public string Sku { get; private set; } = default!;
    public string? CodigoBarras { get; private set; }

    /// <summary>Stock físico en Temixco.</summary>
    public int StockTemixco { get; private set; }

    /// <summary>Stock físico en La Azteca.</summary>
    public int StockAzteca { get; private set; }

    private Variante() { }

    public Variante(
        string id,
        string productoId,
        string talla,
        string colorId,
        string sku,
        int stockTemixco,
        int stockAzteca,
        string? codigoBarras = null)
    {
        if (string.IsNullOrWhiteSpace(talla))
            throw DomnaException.BadRequest("La variante necesita una talla.");
        if (stockTemixco < 0 || stockAzteca < 0)
            throw DomnaException.BadRequest("El stock no puede ser negativo.");

        Id = id;
        ProductoId = productoId;
        Talla = talla.Trim();
        ColorId = colorId;
        Sku = sku;
        StockTemixco = stockTemixco;
        StockAzteca = stockAzteca;
        CodigoBarras = string.IsNullOrWhiteSpace(codigoBarras) ? null : codigoBarras.Trim();
    }

    /// <summary>Stock de una sucursal concreta.</summary>
    public int StockDe(SucursalId sucursal) =>
        sucursal == SucursalId.Temixco ? StockTemixco : StockAzteca;

    /// <summary>Stock físico total (ambas sucursales).</summary>
    public int StockTotal => StockTemixco + StockAzteca;

    /// <summary>Fija el stock absoluto de una sucursal (no delta). Lanza 400 si es negativo.</summary>
    public void FijarStock(SucursalId sucursal, int nuevo)
    {
        if (nuevo < 0)
            throw DomnaException.BadRequest("El stock no puede ser negativo.");
        if (sucursal == SucursalId.Temixco)
            StockTemixco = nuevo;
        else
            StockAzteca = nuevo;
    }

    public void FijarCodigoBarras(string? codigo) =>
        CodigoBarras = string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim();
}
