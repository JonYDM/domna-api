using System.Text.RegularExpressions;

namespace Domna.Domain;

/// <summary>
/// Reglas puras de validación/normalización. Portadas 1:1 de src/mock/clientas.ts del front
/// para que el backend se comporte igual que la demo. Sin dependencias de infraestructura.
/// </summary>
public static partial class Normalizacion
{
    /// <summary>Valida un PIN de 6 dígitos. Lanza 400 si no cumple.</summary>
    public static string NormalizarPin(string pin)
    {
        var p = (pin ?? string.Empty).Trim();
        if (!PinRegex().IsMatch(p))
            throw DomnaException.BadRequest("El PIN debe ser de 6 dígitos.");
        return p;
    }

    /// <summary>
    /// Normaliza a "777 123 4567". Quita todo lo que no sea dígito, descarta el 52 inicial
    /// (lada +52 implícita) y exige 10 dígitos. Lanza 400 si no cumple.
    /// </summary>
    public static string NormalizarTelefono(string telefono)
    {
        var d = NoDigitos().Replace(telefono ?? string.Empty, string.Empty);
        if (d.Length == 12 && d.StartsWith("52"))
            d = d[2..];
        if (d.Length != 10)
            throw DomnaException.BadRequest("El teléfono debe tener 10 dígitos.");
        return $"{d[..3]} {d.Substring(3, 3)} {d[6..]}";
    }

    /// <summary>Normaliza el correo a minúsculas y valida el formato. Lanza 400 si no cumple.</summary>
    public static string NormalizarEmail(string email)
    {
        var v = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (!EmailRegex().IsMatch(v))
            throw DomnaException.BadRequest("Correo inválido.");
        return v;
    }

    /// <summary>Colapsa espacios y recorta. Para nombres/apellidos.</summary>
    public static string NormalizarNombre(string? valor)
    {
        return EspaciosRegex().Replace((valor ?? string.Empty).Trim(), " ");
    }

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex PinRegex();

    [GeneratedRegex(@"\D")]
    private static partial Regex NoDigitos();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRegex();
}
