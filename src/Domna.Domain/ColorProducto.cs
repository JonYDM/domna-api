namespace Domna.Domain;

/// <summary>Color de un producto (copiado del catálogo de la boutique al dar de alta la prenda).</summary>
public class ColorProducto
{
    public string Id { get; private set; } = default!;
    public string ProductoId { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public string Hex { get; private set; } = default!;
    public FamiliaColor Familia { get; private set; }

    private ColorProducto() { }

    public ColorProducto(string id, string productoId, string nombre, string hex, FamiliaColor familia)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw DomnaException.BadRequest("El color necesita un nombre.");
        Id = id;
        ProductoId = productoId;
        Nombre = nombre.Trim();
        Hex = hex.Trim();
        Familia = familia;
    }
}
