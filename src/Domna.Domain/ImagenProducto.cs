namespace Domna.Domain;

/// <summary>
/// Foto de un producto (URL pública en R2). Opcionalmente ligada a un color para que la galería la
/// muestre al elegir ese color. El orden define cuál es la principal (la primera).
/// </summary>
public class ImagenProducto
{
    public int Id { get; private set; }
    public string ProductoId { get; private set; } = default!;
    public string Url { get; private set; } = default!;
    public string? ColorId { get; private set; }
    public int Orden { get; private set; }

    private ImagenProducto() { }

    public ImagenProducto(string productoId, string url, int orden, string? colorId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw DomnaException.BadRequest("La imagen necesita una URL.");
        ProductoId = productoId;
        Url = url.Trim();
        Orden = orden;
        ColorId = string.IsNullOrWhiteSpace(colorId) ? null : colorId;
    }
}
