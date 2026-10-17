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

    public static VarianteDto ToDto(this Variante v, Dictionary<string, int>? reservado = null)
    {
        var resTemixco = reservado?.GetValueOrDefault("temixco") ?? 0;
        var resAzteca = reservado?.GetValueOrDefault("azteca") ?? 0;
        var dispTemixco = Math.Max(0, v.StockDe(SucursalId.Temixco) - resTemixco);
        var dispAzteca = Math.Max(0, v.StockDe(SucursalId.Azteca) - resAzteca);
        return new(
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
            },
            dispTemixco + dispAzteca,
            new Dictionary<string, int> { ["temixco"] = dispTemixco, ["azteca"] = dispAzteca },
            new Dictionary<string, int> { ["temixco"] = resTemixco, ["azteca"] = resAzteca });
    }

    public static ColorProductoDto ToDto(this ColorProducto c) => new(
        c.Id,
        c.Nombre,
        c.Hex,
        EnumMap.Familia(c.Familia));

    public static ProductoDto ToDto(this Producto p, Dictionary<string, Dictionary<string, int>>? reservadoPorVariante = null) => new(
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
        p.Variantes.Select(v => v.ToDto(reservadoPorVariante?.GetValueOrDefault(v.Id))).ToList(),
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

    public static LineaApartadoDto ToDto(this LineaApartado l, string? imagenUrl = null) => new(
        l.VarianteId,
        l.ProductoId,
        l.Nombre,
        EnumMap.Silueta(l.Silueta),
        l.Talla,
        l.ColorNombre,
        l.ColorHex,
        l.Precio,
        l.Costo,
        l.Cantidad,
        EnumMap.Sucursal(l.SucursalOrigen),
        imagenUrl);

    public static AbonoDto ToDto(this Abono a) => new(
        a.Id,
        a.Monto,
        a.Fecha,
        EnumMap.Metodo(a.Metodo),
        EnumMap.Concepto(a.Concepto),
        EnumMap.EstadoPago(a.Estado),
        a.RevisadoPor,
        a.RevisadoEl,
        a.MotivoRechazo,
        a.TieneComprobante);

    /// <param name="fotoDe">Cómo se obtiene la foto de cada línea (la resuelve el servicio, que sí consulta fotos).</param>
    public static ApartadoDto ToDto(this Apartado a, Func<LineaApartado, string?>? fotoDe = null) => new(
        a.Id,
        a.Folio,
        a.ClientaId,
        a.ClientaNombre,
        a.ClientaTelefono,
        a.Lineas.Select(l => l.ToDto(fotoDe?.Invoke(l))).ToList(),
        a.Subtotal,
        a.Penalizacion,
        a.Total,
        EnumMap.Modalidad(a.Modalidad),
        EnumMap.Canal(a.Canal),
        EnumMap.Sucursal(a.Entrega),
        a.RequiereTraslado,
        EnumMap.EstadoEntrega(a.EstadoEntrega),
        a.Abonos.Select(ab => ab.ToDto()).ToList(),
        a.Pagado,
        a.PorConfirmar,
        a.Saldo,
        a.Creado,
        a.VenceEl,
        a.ListoEstimado,
        EnumMap.EstadoApartado(a.Estado),
        a.CerradoEl);
}
