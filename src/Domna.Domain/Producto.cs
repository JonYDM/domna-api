using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Domna.Domain;

/// <summary>
/// Agregado raíz Prenda. Al darla de alta se generan las variantes como el producto cartesiano
/// talla × color, cada una con su SKU y su stock independiente por sucursal. Alineado con la
/// interfaz Producto y el input NuevoProductoInput de src/types/api.ts.
/// </summary>
public partial class Producto
{
    private readonly List<ColorProducto> _colores = new();
    private readonly List<Variante> _variantes = new();

    public string Id { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public string Descripcion { get; private set; } = default!;
    public string CategoriaId { get; private set; } = default!;
    public Silueta Silueta { get; private set; }
    public decimal Precio { get; private set; }
    public decimal Costo { get; private set; }
    public decimal? PrecioAntes { get; private set; }
    public string? CodigoBarras { get; private set; }
    public bool Activo { get; private set; }
    public bool PermiteApartado { get; private set; }
    public DateTime Creado { get; private set; }
    public DateTime? ReabastecidoEl { get; private set; }

    public IReadOnlyList<ColorProducto> Colores => _colores.AsReadOnly();
    public IReadOnlyList<Variante> Variantes => _variantes.AsReadOnly();

    /// <summary>Tallas únicas de las variantes (en el orden en que se dieron de alta).</summary>
    public IReadOnlyList<string> Tallas => _variantes.Select(v => v.Talla).Distinct().ToList();

    private Producto() { }

    /// <summary>
    /// Da de alta una prenda nueva: valida los datos base, crea los colores y genera las variantes
    /// talla × color con stock por sucursal y código de barras opcional por variante.
    /// </summary>
    /// <param name="stock">stock[sucursal][talla] = arreglo paralelo a los colores (stock[talla][colorIndex]).</param>
    /// <param name="codigoBarras">codigoBarras[talla] = arreglo paralelo a los colores (opcional).</param>
    public static Producto Crear(
        string id,
        string nombre,
        string? descripcion,
        string categoriaId,
        Silueta silueta,
        decimal precio,
        decimal costo,
        bool permiteApartado,
        IReadOnlyList<ColorDraft> colores,
        IReadOnlyList<string> tallas,
        IReadOnlyDictionary<SucursalId, IReadOnlyDictionary<string, int[]>> stock,
        DateTime ahora,
        IReadOnlyDictionary<string, string?[]>? codigoBarras = null,
        Func<string>? nuevoIdColor = null,
        Func<string>? nuevoIdVariante = null)
    {
        var nombreNorm = Normalizacion.NormalizarNombre(nombre);
        if (nombreNorm.Length < 2)
            throw DomnaException.BadRequest("Escribe el nombre de la prenda.");
        if (string.IsNullOrWhiteSpace(categoriaId))
            throw DomnaException.BadRequest("Elige una categoría.");
        if (precio <= 0)
            throw DomnaException.BadRequest("El precio debe ser mayor a cero.");
        if (costo < 0)
            throw DomnaException.BadRequest("El costo no puede ser negativo.");
        if (colores.Count == 0)
            throw DomnaException.BadRequest("Agrega al menos un color.");
        if (tallas.Count == 0)
            throw DomnaException.BadRequest("Agrega al menos una talla.");

        var idColor = nuevoIdColor ?? (() => Guid.NewGuid().ToString("N")[..8]);
        var idVariante = nuevoIdVariante ?? (() => Guid.NewGuid().ToString("N")[..8]);

        var p = new Producto
        {
            Id = id,
            Nombre = nombreNorm,
            Descripcion = (descripcion ?? string.Empty).Trim(),
            CategoriaId = categoriaId,
            Silueta = silueta,
            Precio = precio,
            Costo = costo,
            Activo = true,
            PermiteApartado = permiteApartado,
            Creado = ahora
        };

        // Colores (se crean primero para asignar su id a las variantes).
        foreach (var c in colores)
            p._colores.Add(new ColorProducto(idColor(), id, c.Nombre, c.Hex, c.Familia));

        var slug = Slug(nombreNorm);

        // Producto cartesiano talla × color.
        foreach (var talla in tallas)
        {
            for (var ci = 0; ci < p._colores.Count; ci++)
            {
                var color = p._colores[ci];
                var tStock = stock.TryGetValue(SucursalId.Temixco, out var temixco)
                    && temixco.TryGetValue(talla, out var arrT) && ci < arrT.Length ? arrT[ci] : 0;
                var aStock = stock.TryGetValue(SucursalId.Azteca, out var azteca)
                    && azteca.TryGetValue(talla, out var arrA) && ci < arrA.Length ? arrA[ci] : 0;

                string? codigo = null;
                if (codigoBarras is not null
                    && codigoBarras.TryGetValue(talla, out var arrCb) && ci < arrCb.Length)
                    codigo = arrCb[ci];

                var sku = $"{slug}-{Slug(talla)}-{Slug(color.Nombre)}".ToUpperInvariant();
                p._variantes.Add(new Variante(idVariante(), id, talla, color.Id, sku, tStock, aStock, codigo));
            }
        }

        return p;
    }

    public void Renombrar(string nombre)
    {
        var n = Normalizacion.NormalizarNombre(nombre);
        if (n.Length < 2)
            throw DomnaException.BadRequest("Escribe el nombre de la prenda.");
        Nombre = n;
    }

    public void CambiarDescripcion(string? descripcion) =>
        Descripcion = (descripcion ?? string.Empty).Trim();

    public void CambiarPrecio(decimal precio)
    {
        if (precio <= 0)
            throw DomnaException.BadRequest("El precio debe ser mayor a cero.");
        Precio = precio;
    }

    public void CambiarCosto(decimal costo)
    {
        if (costo < 0)
            throw DomnaException.BadRequest("El costo no puede ser negativo.");
        Costo = costo;
    }

    public void CambiarPermiteApartado(bool permite) => PermiteApartado = permite;

    public void CambiarPrecioAntes(decimal? precioAntes)
    {
        if (precioAntes is not null && precioAntes <= 0)
            throw DomnaException.BadRequest("El precio anterior debe ser mayor a cero.");
        PrecioAntes = precioAntes;
    }

    /// <summary>Baja lógica: el producto deja de verse en el catálogo pero conserva su historial.</summary>
    public void Desactivar() => Activo = false;
    public void Activar() => Activo = true;

    /// <summary>Ajusta el stock absoluto de una variante en una sucursal. Lanza 404 si no existe.</summary>
    public void AjustarStock(string varianteId, SucursalId sucursal, int nuevo, DateTime ahora)
    {
        var v = _variantes.FirstOrDefault(x => x.Id == varianteId)
            ?? throw DomnaException.NotFound("Variante no encontrada.");
        var antes = v.StockTotal;
        v.FijarStock(sucursal, nuevo);
        // Si una variante agotada vuelve a tener piezas, marca el reabastecimiento (para "De vuelta en stock").
        if (antes == 0 && v.StockTotal > 0)
            ReabastecidoEl = ahora;
    }

    public Variante? BuscarPorCodigoBarras(string codigo) =>
        _variantes.FirstOrDefault(v => v.CodigoBarras == codigo);

    /// <summary>Convierte un texto a slug ASCII para el SKU (sin acentos, mayúsculas, con guiones).</summary>
    private static string Slug(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var limpio = NoAlfaNum().Replace(sb.ToString(), "-").Trim('-');
        return limpio.Length == 0 ? "x" : limpio;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex NoAlfaNum();
}

/// <summary>Datos de un color al dar de alta la prenda (aún sin id, lo asigna el agregado).</summary>
public record ColorDraft(string Nombre, string Hex, FamiliaColor Familia);
