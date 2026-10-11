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
}
