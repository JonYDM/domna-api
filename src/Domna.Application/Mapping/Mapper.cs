using Domna.Application.Dtos;
using Domna.Domain;

namespace Domna.Application.Mapping;

/// <summary>Proyecta entidades de dominio a los DTOs del contrato.</summary>
public static class Mapper
{
    public static ClientaDto ToDto(this Clienta c) => new(
        c.Id,
        c.Nombre,
        c.Apellidos,
        c.Telefono,
        c.Email,
        EnumMap.Origen(c.Origen),
        EnumMap.Rol(c.Rol),
        c.SucursalCajera is null ? null : EnumMap.Sucursal(c.SucursalCajera.Value),
        c.Creada,
        c.UltimoAcceso,
        c.PenalizacionPendiente,
        c.Activo);

    public static VarianteDto ToDto(this Variante v) => new(
        v.Id,
        v.ProductoId,
        v.Talla,
        v.ColorId,
        v.Sku,
        v.CodigoBarras,
        new Dictionary<string, int>
        {
            ["temixco"] = v.StockDe(SucursalId.Temixco),
            ["azteca"] = v.StockDe(SucursalId.Azteca)
        });

    public static ColorProductoDto ToDto(this ColorProducto c) => new(
        c.Id,
        c.Nombre,
        c.Hex,
        EnumMap.Familia(c.Familia));

    public static ProductoDto ToDto(this Producto p) => new(
        p.Id,
        p.Nombre,
        p.Descripcion,
        p.CategoriaId,
        EnumMap.Silueta(p.Silueta),
        p.Precio,
        p.Costo,
        p.PrecioAntes,
        p.CodigoBarras,
        p.Activo,
        p.PermiteApartado,
        p.Colores.Select(c => c.ToDto()).ToList(),
        p.Tallas.ToList(),
        p.Variantes.Select(v => v.ToDto()).ToList(),
        p.Imagenes.OrderBy(i => i.Orden).Select(i => new ImagenProductoDto(i.Url, i.ColorId)).ToList(),
        p.Creado,
        p.ReabastecidoEl);

    public static CategoriaDto ToDto(this Categoria c, int productos) => new(
        c.Id,
        c.Nombre,
        c.Tallas.ToList(),
        productos);

    public static ColorCatalogoDto ToDto(this ColorCatalogo c) => new(
        c.Id,
        c.Nombre,
        c.Hex,
        EnumMap.Familia(c.Familia));
}
