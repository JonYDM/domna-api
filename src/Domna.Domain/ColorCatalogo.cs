namespace Domna.Domain;

/// <summary>
/// Color del catálogo de la boutique. Las prendas COPIAN el color al darse de alta, así que editar
/// o borrar un color del catálogo no rompe prendas existentes. La familia se calcula del hex.
/// </summary>
public class ColorCatalogo
{
    public string Id { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public string Hex { get; private set; } = default!;
    public FamiliaColor Familia { get; private set; }

    private ColorCatalogo() { }

    public static ColorCatalogo Crear(string id, string nombre, string hex)
    {
        var h = Atributos.NormalizarHex(hex);
        return new ColorCatalogo
        {
            Id = id,
            Nombre = Atributos.LimpiarNombre(nombre, 2, 24),
            Hex = h,
            Familia = Atributos.FamiliaDeHex(h)
        };
    }

    public void Actualizar(string nombre, string hex)
    {
        Nombre = Atributos.LimpiarNombre(nombre, 2, 24);
        Hex = Atributos.NormalizarHex(hex);
        Familia = Atributos.FamiliaDeHex(Hex);
    }
}
