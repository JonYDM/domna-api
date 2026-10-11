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
    DateTime Creado,
    DateTime? ReabastecidoEl);

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
    Dictionary<string, string?[]>? CodigoBarras);

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
