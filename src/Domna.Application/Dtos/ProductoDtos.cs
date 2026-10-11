namespace Domna.Application.Dtos;

/// <summary>Color de un producto.</summary>
public record ColorProductoDto(string Id, string Nombre, string Hex, string Familia);

/// <summary>Variante con stock por sucursal (clave "temixco"/"azteca" como en el front).</summary>
public record VarianteDto(
    string Id,
    string ProductoId,
    string Talla,
    string ColorId,
    string Sku,
    string? CodigoBarras,
    Dictionary<string, int> Stock);

/// <summary>Producto completo con sus colores y variantes. Equivale a la interfaz Producto del front.</summary>
public record ProductoDto(
    string Id,
    string Nombre,
    string Descripcion,
    string CategoriaId,
    string Silueta,
    decimal Precio,
    decimal Costo,
    decimal? PrecioAntes,
    string? CodigoBarras,
    bool Activo,
    bool PermiteApartado,
    List<ColorProductoDto> Colores,
    List<string> Tallas,
    List<VarianteDto> Variantes,
    List<ImagenProductoDto> Imagenes,
    DateTime Creado,
    DateTime? ReabastecidoEl);

/// <summary>Foto del producto (URL pública), opcionalmente ligada a un color.</summary>
public record ImagenProductoDto(string Url, string? ColorId);

/// <summary>Color al dar de alta (sin id; el servidor lo asigna).</summary>
public record ColorInputDto(string Nombre, string Hex, string Familia);

/// <summary>
/// Alta de prenda. Equivale a NuevoProductoInput de api.ts:
/// stock[sucursal][talla] = arreglo paralelo a `colores` (stock[talla][colorIndex]);
/// codigoBarras[talla] = arreglo paralelo a `colores` (opcional).
/// </summary>
public record NuevoProductoInput(
    string Nombre,
    string Descripcion,
    string CategoriaId,
    string Silueta,
    decimal Precio,
    decimal Costo,
    List<ColorInputDto> Colores,
    List<string> Tallas,
    Dictionary<string, Dictionary<string, int[]>> Stock,
    bool PermiteApartado,
    Dictionary<string, string?[]>? CodigoBarras,
    List<ImagenInputDto>? Imagenes = null);

/// <summary>
/// Una foto a guardar (URL pública en R2). Se liga a un color por su ÍNDICE en la lista `Colores`
/// del input (el front no conoce los ids que el backend genera). `Orden` ordena las fotos del color.
/// </summary>
public record ImagenInputDto(string Url, int? ColorIndex, int Orden);

/// <summary>Edición de los campos simples de la prenda (no toca variantes/stock).</summary>
public record EditarProductoInput(
    string Nombre,
    string Descripcion,
    decimal Precio,
    decimal Costo,
    decimal? PrecioAntes,
    bool PermiteApartado);

/// <summary>Ajuste de stock absoluto de una variante en una sucursal.</summary>
public record AjusteStockInput(string VarianteId, string Sucursal, int Nuevo);

/// <summary>Petición para firmar la subida de las fotos de un producto a R2.</summary>
public record FirmarImagenesInput(string ProductoId, List<string>? Tamanos, string? ContentType);

/// <summary>
/// Agregar un color a un producto existente, con sus tallas, stock por sucursal y fotos.
/// stock[sucursal][talla] = arreglo de 1 (el stock de esa talla en ese color).
/// </summary>
public record AgregarColorInput(
    ColorInputDto Color,
    List<string> Tallas,
    Dictionary<string, Dictionary<string, int[]>> Stock,
    List<string>? Fotos);

/// <summary>Quitar una foto de la galería por su URL pública.</summary>
public record QuitarImagenInput(string Url);

/// <summary>Agregar fotos (URLs ya subidas a R2) a un color existente.</summary>
public record AgregarImagenesInput(List<string> Urls);
