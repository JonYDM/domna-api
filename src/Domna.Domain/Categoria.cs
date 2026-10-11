namespace Domna.Domain;

/// <summary>
/// Categoría del catálogo con su juego de tallas. Las tallas viven aquí (no son entidad suelta):
/// al dar de alta una prenda, el wizard propone las tallas de su categoría. Portada de atributos.ts.
/// </summary>
public class Categoria
{
    private readonly List<string> _tallas = new();

    public string Id { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public IReadOnlyList<string> Tallas => _tallas.AsReadOnly();

    private Categoria() { }

    public static Categoria Crear(string id, string nombre, IEnumerable<string> tallas)
    {
        var c = new Categoria
        {
            Id = id,
            Nombre = Atributos.LimpiarNombre(nombre)
        };
        c._tallas.AddRange(Atributos.NormalizarTallas(tallas));
        return c;
    }

    /// <summary>Editar no toca prendas existentes: las tallas nuevas solo se proponen en altas futuras.</summary>
    public void Actualizar(string nombre, IEnumerable<string> tallas)
    {
        Nombre = Atributos.LimpiarNombre(nombre);
        _tallas.Clear();
        _tallas.AddRange(Atributos.NormalizarTallas(tallas));
    }

    /// <summary>¿La talla pertenece a esta categoría? (comparación ya normalizada).</summary>
    public bool TieneTalla(string talla) => _tallas.Contains(talla);
}
