namespace Domna.Domain;

/// <summary>
/// Arma un texto corto y legible del dispositivo a partir del User-Agent, para que el front muestre
/// "¿En cuál dispositivo quieres seguir?" (p. ej. "Android · Chrome", "iPhone · Safari",
/// "Windows · Edge", "Mac · Safari"). Es una función pura: no toca red ni estado.
///
/// No pretende ser un parser exhaustivo de User-Agents: solo cubre los casos comunes del front
/// (móviles y escritorio) y cae a "Otro dispositivo" cuando no reconoce nada.
/// </summary>
public static class DescripcionDispositivo
{
    public static string De(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Otro dispositivo";

        var ua = userAgent;
        var so = DetectarSo(ua);
        var navegador = DetectarNavegador(ua);

        if (so is null && navegador is null) return "Otro dispositivo";
        if (so is null) return navegador!;
        if (navegador is null) return so;
        return $"{so} · {navegador}";
    }

    private static string? DetectarSo(string ua)
    {
        // iPhone/iPad antes que "Mac": el iPad moderno a veces se anuncia como Macintosh, pero
        // la mayoría de iPhones/iPads traen el token explícito.
        if (Contiene(ua, "iPhone")) return "iPhone";
        if (Contiene(ua, "iPad")) return "iPad";
        if (Contiene(ua, "Android")) return "Android";
        if (Contiene(ua, "Windows")) return "Windows";
        // "Macintosh"/"Mac OS X" pero NO iOS (ya cubierto arriba).
        if (Contiene(ua, "Macintosh") || Contiene(ua, "Mac OS")) return "Mac";
        if (Contiene(ua, "Linux")) return "Linux";
        return null;
    }

    private static string? DetectarNavegador(string ua)
    {
        // El orden importa: Edge y Chrome también incluyen "Safari"/"Chrome" en su UA.
        if (Contiene(ua, "Edg")) return "Edge";               // Edge Chromium: "Edg/..."
        if (Contiene(ua, "OPR") || Contiene(ua, "Opera")) return "Opera";
        if (Contiene(ua, "Firefox") || Contiene(ua, "FxiOS")) return "Firefox";
        if (Contiene(ua, "CriOS")) return "Chrome";           // Chrome en iOS
        if (Contiene(ua, "Chrome") || Contiene(ua, "Chromium")) return "Chrome";
        if (Contiene(ua, "Safari")) return "Safari";
        return null;
    }

    private static bool Contiene(string texto, string valor) =>
        texto.Contains(valor, StringComparison.OrdinalIgnoreCase);
}
