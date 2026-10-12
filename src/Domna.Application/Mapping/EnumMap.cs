using Domna.Domain;

namespace Domna.Application.Mapping;

/// <summary>
/// Convierte entre los strings del contrato del front (minúsculas: "temixco", "vestido", "registro")
/// y los enums de dominio. Centraliza el mapeo para no repetir switches.
/// </summary>
public static class EnumMap
{
    public static SucursalId Sucursal(string valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "temixco" => SucursalId.Temixco,
        "azteca" => SucursalId.Azteca,
        _ => throw DomnaException.BadRequest($"Sucursal inválida: '{valor}'.")
    };

    public static string Sucursal(SucursalId valor) => valor switch
    {
        SucursalId.Temixco => "temixco",
        SucursalId.Azteca => "azteca",
        _ => throw new ArgumentOutOfRangeException(nameof(valor))
    };

    public static Silueta Silueta(string valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "vestido" => Domain.Silueta.Vestido,
        "blusa" => Domain.Silueta.Blusa,
        "pantalon" => Domain.Silueta.Pantalon,
        "falda" => Domain.Silueta.Falda,
        "chamarra" => Domain.Silueta.Chamarra,
        "bolsa" => Domain.Silueta.Bolsa,
        "conjunto" => Domain.Silueta.Conjunto,
        _ => throw DomnaException.BadRequest($"Silueta inválida: '{valor}'.")
    };

    public static string Silueta(Silueta valor) => valor.ToString().ToLowerInvariant();

    public static FamiliaColor Familia(string valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "negro" => FamiliaColor.Negro,
        "blanco" => FamiliaColor.Blanco,
        "beige" => FamiliaColor.Beige,
        "cafe" => FamiliaColor.Cafe,
        "gris" => FamiliaColor.Gris,
        "rojo" => FamiliaColor.Rojo,
        "rosa" => FamiliaColor.Rosa,
        "azul" => FamiliaColor.Azul,
        "verde" => FamiliaColor.Verde,
        _ => throw DomnaException.BadRequest($"Familia de color inválida: '{valor}'.")
    };

    public static string Familia(FamiliaColor valor) => valor.ToString().ToLowerInvariant();

    public static string Origen(OrigenClienta valor) => valor.ToString().ToLowerInvariant();

    public static string Rol(Rol valor) => valor.ToString().ToLowerInvariant();

    public static ModalidadApartado Modalidad(string valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "sin_anticipo" => ModalidadApartado.SinAnticipo,
        "anticipo" => ModalidadApartado.Anticipo,
        "compra" => ModalidadApartado.Compra,
        _ => throw DomnaException.BadRequest($"Modalidad inválida: '{valor}'.")
    };

    public static string Modalidad(ModalidadApartado valor) => valor switch
    {
        ModalidadApartado.SinAnticipo => "sin_anticipo",
        ModalidadApartado.Anticipo => "anticipo",
        ModalidadApartado.Compra => "compra",
        _ => throw new ArgumentOutOfRangeException(nameof(valor))
    };

    public static Canal Canal(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "fisico" => Domain.Canal.Fisico,
        _ => Domain.Canal.Online
    };

    public static string Canal(Canal valor) => valor == Domain.Canal.Fisico ? "fisico" : "online";

    public static string EstadoApartado(EstadoApartado valor) => valor.ToString().ToLowerInvariant();

    public static EstadoEntrega EstadoEntrega(string valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "en_origen" => Domain.EstadoEntrega.EnOrigen,
        "en_traslado" => Domain.EstadoEntrega.EnTraslado,
        "listo" => Domain.EstadoEntrega.Listo,
        "entregado" => Domain.EstadoEntrega.Entregado,
        _ => throw DomnaException.BadRequest($"Estado de entrega inválido: '{valor}'.")
    };

    public static string EstadoEntrega(EstadoEntrega valor) => valor switch
    {
        Domain.EstadoEntrega.EnOrigen => "en_origen",
        Domain.EstadoEntrega.EnTraslado => "en_traslado",
        Domain.EstadoEntrega.Listo => "listo",
        Domain.EstadoEntrega.Entregado => "entregado",
        _ => throw new ArgumentOutOfRangeException(nameof(valor))
    };

    public static MetodoPago Metodo(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "efectivo" => MetodoPago.Efectivo,
        "tarjeta" => MetodoPago.Tarjeta,
        "transferencia" => MetodoPago.Transferencia,
        null => MetodoPago.Transferencia,
        _ => throw DomnaException.BadRequest($"Método de pago inválido: '{valor}'.")
    };

    public static string Metodo(MetodoPago valor) => valor.ToString().ToLowerInvariant();

    public static string Concepto(ConceptoAbono valor) => valor.ToString().ToLowerInvariant();
}
