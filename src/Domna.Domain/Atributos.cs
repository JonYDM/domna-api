using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Domna.Domain;

/// <summary>
/// Reglas puras del catálogo de atributos (categorías/tallas y colores). Portadas de
/// domna-web/src/mock/atributos.ts y src/lib/color.ts para que el backend valide igual que la demo.
/// </summary>
public static partial class Atributos
{
    private static readonly string[] PalabrasTamano = { "chica", "mediana", "grande", "única", "unica" };

    /// <summary>Slug ASCII (sin acentos, minúsculas, con guiones) para id y comparación de nombres.</summary>
    public static string Slug(string texto)
    {
        var normalizado = (texto ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalizado)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        var limpio = NoAlfaNum().Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
        return limpio;
    }

    /// <summary>¿Dos nombres son "el mismo" (ignorando mayúsculas, acentos y espacios)?</summary>
    public static bool MismoNombre(string a, string b) => Slug(a) == Slug(b);

    /// <summary>Recorta, colapsa espacios y valida longitud mínima/máxima.</summary>
    public static string LimpiarNombre(string nombre, int min = 2, int max = 30)
    {
        var n = Espacios().Replace((nombre ?? string.Empty).Trim(), " ");
        if (n.Length < min) throw DomnaException.BadRequest("Escribe un nombre.");
        if (n.Length > max) throw DomnaException.BadRequest($"Máximo {max} caracteres.");
        return n;
    }

    private static string Capitalizar(string v)
    {
        var low = v.ToLowerInvariant();
        if (low is "unica" or "única") return "Única";
        return char.ToUpperInvariant(low[0]) + low[1..];
    }

    /// <summary>
    /// Normaliza tallas: sin espacios extra, sin repetir, 1–15 por categoría.
    /// - Códigos cortos ("ch", "m") → MAYÚSCULAS, máx. 6 caracteres.
    /// - Palabras de tamaño ("mediana", "grande", "única") → Capitalizadas, máx. 10 caracteres.
    /// </summary>
    public static List<string> NormalizarTallas(IEnumerable<string> tallas)
    {
        var outp = new List<string>();
        foreach (var t in tallas ?? Enumerable.Empty<string>())
        {
            var v = Espacios().Replace((t ?? string.Empty).Trim(), " ");
            if (string.IsNullOrEmpty(v)) continue;
            var esPalabra = PalabrasTamano.Contains(v.ToLowerInvariant());
            var norm = esPalabra ? Capitalizar(v) : v.ToUpperInvariant();
            var maxLen = esPalabra ? 10 : 6;
            if (norm.Length > maxLen)
                throw DomnaException.BadRequest($"La talla \"{v}\" es muy larga (máx. {maxLen}).");
            if (!outp.Contains(norm)) outp.Add(norm);
        }
        if (outp.Count == 0) throw DomnaException.BadRequest("Agrega al menos una talla.");
        if (outp.Count > 15) throw DomnaException.BadRequest("Máximo 15 tallas por categoría.");
        return outp;
    }

    /// <summary>Normaliza el hex: acepta #RGB (lo expande a #RRGGBB) o #RRGGBB. Devuelve "#RRGGBB" en mayúsculas.</summary>
    public static string NormalizarHex(string hex)
    {
        var h = (hex ?? string.Empty).Trim().ToUpperInvariant();
        var corto = HexCorto().Match(h);
        if (corto.Success)
            return $"#{corto.Groups[1].Value}{corto.Groups[1].Value}{corto.Groups[2].Value}{corto.Groups[2].Value}{corto.Groups[3].Value}{corto.Groups[3].Value}";
        var largo = HexLargo().Match(h);
        if (!largo.Success)
            throw DomnaException.BadRequest("Color inválido (usa formato #RRGGBB).");
        return $"#{largo.Groups[1].Value}";
    }

    /// <summary>Grupo (familia) del color a partir del tono, portado de familiaDeHex. Espera "#RRGGBB".</summary>
    public static FamiliaColor FamiliaDeHex(string hex)
    {
        var limpio = hex.Replace("#", "");
        var n = int.Parse(limpio, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double r = ((n >> 16) & 255) / 255.0;
        double g = ((n >> 8) & 255) / 255.0;
        double b = (n & 255) / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var croma = max - min;

        if (l < 0.18) return FamiliaColor.Negro;
        if (l > 0.86) return FamiliaColor.Blanco;
        if (croma < 0.12) return l > 0.75 ? FamiliaColor.Blanco : FamiliaColor.Gris;

        double h;
        if (max == r) h = ((g - b) / croma) % 6;
        else if (max == g) h = (b - r) / croma + 2;
        else h = (r - g) / croma + 4;
        h = (h * 60 + 360) % 360;

        if (h >= 345 || h < 15) return l > 0.68 ? FamiliaColor.Rosa : FamiliaColor.Rojo;
        if (h < 50) return l < 0.6 ? FamiliaColor.Cafe : FamiliaColor.Beige;
        if (h < 62) return FamiliaColor.Beige;
        if (h < 170) return FamiliaColor.Verde;
        if (h < 260) return FamiliaColor.Azul;
        return l > 0.55 ? FamiliaColor.Rosa : FamiliaColor.Rojo;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NoAlfaNum();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"^#?([0-9A-F])([0-9A-F])([0-9A-F])$")]
    private static partial Regex HexCorto();

    [GeneratedRegex(@"^#?([0-9A-F]{6})$")]
    private static partial Regex HexLargo();
}
