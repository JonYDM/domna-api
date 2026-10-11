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
    private readonly List<ImagenProducto> _imagenes = new();

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
    public IReadOnlyList<ImagenProducto> Imagenes => _imagenes.AsReadOnly();

    /// <summary>Reemplaza las fotos del producto (URLs públicas en R2) en el orden dado.</summary>
    public void FijarImagenes(IEnumerable<(string Url, string? ColorId)> imagenes)
    {
        _imagenes.Clear();
        var i = 0;
        foreach (var (url, colorId) in imagenes)
            _imagenes.Add(new ImagenProducto(Id, url, i++, colorId));
    }

    /// <summary>Cuántas fotos tiene un color.</summary>
    public int FotosDeColor(string colorId) => _imagenes.Count(i => i.ColorId == colorId);

    /// <summary>Quita UNA foto por su URL. No valida reglas de "última foto" (eso lo decide el caso de uso).</summary>
    public void QuitarImagen(string url)
    {
        var img = _imagenes.FirstOrDefault(i => i.Url == url)
            ?? throw DomnaException.NotFound("Esa foto no existe en la prenda.");
        _imagenes.Remove(img);
    }

    /// <summary>Agrega fotos (URLs ya subidas) a un color existente, continuando el orden.</summary>
    public void AgregarImagenesAColor(string colorId, IEnumerable<string> urls)
    {
        if (_colores.All(c => c.Id != colorId))
            throw DomnaException.NotFound("El color no existe en la prenda.");
        var orden = _imagenes.Count;
        foreach (var url in urls)
            _imagenes.Add(new ImagenProducto(Id, url, orden++, colorId));
    }

    /// <summary>
    /// Quita un color de la prenda: sus variantes y sus fotos. Lo usa "quitar la última foto de un
    /// color". `tieneApartados` lo decide el caso de uso (cuando exista el módulo de apartados); si es
    /// true, no se puede borrar para no romper pedidos. Debe quedar al menos un color.
    /// </summary>
    public void QuitarColor(string colorId, bool tieneApartados)
    {
        var color = _colores.FirstOrDefault(c => c.Id == colorId)
            ?? throw DomnaException.NotFound("El color no existe en la prenda.");
        if (_colores.Count <= 1)
            throw DomnaException.Conflict("No puedes quitar el único color de la prenda.");
        if (tieneApartados)
            throw DomnaException.Conflict("Este color tiene apartados activos; no se puede quitar.");

        _variantes.RemoveAll(v => v.ColorId == colorId);
        _imagenes.RemoveAll(i => i.ColorId == colorId);
        _colores.Remove(color);
    }

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

                // El código del QR lo genera SIEMPRE el servidor (nunca se teclea ni se recibe).
                var codigo = GenerarCodigo();

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

    /// <summary>
    /// Agrega un color nuevo al producto con sus variantes (color × tallas), su stock por sucursal
    /// y sus fotos. Usado por el edit ("Agregar color" tipo Zara). Rechaza colores/tallas duplicados.
    /// </summary>
    public void AgregarColor(
        ColorDraft color,
        IReadOnlyList<string> tallas,
        IReadOnlyDictionary<SucursalId, IReadOnlyDictionary<string, int[]>> stock,
        IReadOnlyList<string> fotos,
        DateTime ahora,
        Func<string>? nuevoIdColor = null,
        Func<string>? nuevoIdVariante = null)
    {
        if (tallas.Count == 0)
            throw DomnaException.BadRequest("Agrega al menos una talla para el color.");
        if (_colores.Any(c => string.Equals(c.Nombre, color.Nombre.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw DomnaException.Conflict($"El color «{color.Nombre}» ya existe en esta prenda.");

        var idColor = nuevoIdColor ?? (() => Guid.NewGuid().ToString("N")[..8]);
        var idVariante = nuevoIdVariante ?? (() => Guid.NewGuid().ToString("N")[..8]);

        var nuevo = new ColorProducto(idColor(), Id, color.Nombre, color.Hex, color.Familia);
        _colores.Add(nuevo);

        var slug = Slug(Nombre);
        foreach (var talla in tallas)
        {
            var tStock = stock.TryGetValue(SucursalId.Temixco, out var t) && t.TryGetValue(talla, out var at) && at.Length > 0 ? at[0] : 0;
            var aStock = stock.TryGetValue(SucursalId.Azteca, out var a) && a.TryGetValue(talla, out var aa) && aa.Length > 0 ? aa[0] : 0;
            var sku = $"{slug}-{Slug(talla)}-{Slug(nuevo.Nombre)}".ToUpperInvariant();
            _variantes.Add(new Variante(idVariante(), Id, talla, nuevo.Id, sku, tStock, aStock, GenerarCodigo()));
        }

        // Fotos del nuevo color, continuando el orden global.
        var orden = _imagenes.Count;
        foreach (var url in fotos)
            _imagenes.Add(new ImagenProducto(Id, url, orden++, nuevo.Id));

        // Si una variante nueva nace con stock y antes no había de ese color, cuenta como novedad de stock.
        if (_variantes.Any(v => v.ColorId == nuevo.Id && v.StockTotal > 0))
            ReabastecidoEl = ahora;
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

    /// <summary>
    /// Genera un código único por variante para el QR (ya no se teclea). Formato corto y escaneable:
    /// "DMN-" + 10 chars base36 de un GUID. Único en la práctica; el POS lo busca tal cual.
    /// </summary>
    private static string GenerarCodigo()
    {
        var g = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"DMN-{g[..10]}";
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex NoAlfaNum();
}

/// <summary>Datos de un color al dar de alta la prenda (aún sin id, lo asigna el agregado).</summary>
public record ColorDraft(string Nombre, string Hex, FamiliaColor Familia);
