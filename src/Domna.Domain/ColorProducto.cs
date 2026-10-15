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

    /// <summary>
    /// ¿Esta copia es del color del catálogo que se llama así? Se reconoce por NOMBRE: una copia solo se
    /// aparta del catálogo cuando éste se edita después, y en ese caso sigue siendo "el mismo color".
    /// </summary>
    public bool EsDe(string nombre) => string.Equals(Nombre, nombre.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Esta copia se ve distinta (tono) a como está hoy el color en el catálogo?</summary>
    public bool TonoDistinto(string hex) => !string.Equals(Hex, hex.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <returns>true si algo cambió.</returns>
    internal bool Reemplazar(string nombre, string hex, FamiliaColor familia)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw DomnaException.BadRequest("El color necesita un nombre.");
        var n = nombre.Trim();
        var h = hex.Trim();
        var cambio = !string.Equals(Nombre, n, StringComparison.Ordinal) || !string.Equals(Hex, h, StringComparison.OrdinalIgnoreCase) || Familia != familia;
        Nombre = n;
        Hex = h;
        Familia = familia;
        return cambio;
    }
}
