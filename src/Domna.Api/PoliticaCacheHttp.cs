using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Domna.Api;

/// <summary>
/// Política de caché HTTP "denegar por defecto" + lista blanca explícita. Una sola regla global
/// rompería la app (mezclaría datos privados en caches compartidas), así que:
///  - Toda respuesta bajo <c>/api</c> sale con <c>Cache-Control: no-store</c> salvo que el endpoint
///    o una regla de la lista blanca ponga la suya.
///  - Solo un puñado de GET públicos (catálogo, categorías, colores, config) se cachean, y con
///    reglas pensadas una por una.
/// Esta clase NO escribe en la respuesta: solo decide la regla. El middleware la aplica.
/// </summary>
public static class PoliticaCacheHttp
{
    /// <summary>Cabecera por defecto para todo <c>/api</c> no incluido en la lista blanca.</summary>
    public const string PorDefecto = "no-store";

    /// <summary>Catálogo público: cacheable breve con revalidación en segundo plano.</summary>
    public const string Publico = "public, max-age=30, stale-while-revalidate=120";

    /// <summary>El panel de la dueña ve inactivos: jamás en cache compartida.</summary>
    public const string PrivadoNoStore = "private, no-store";

    /// <summary>Config: siempre revalidar (trae el interruptor de tienda suspendida).</summary>
    public const string NoCache = "no-cache";

    /// <summary>
    /// Decide la regla de caché para una petición. Devuelve:
    ///  - <c>null</c> si la ruta NO está en la lista blanca (el middleware aplicará el no-store por
    ///    defecto, salvo que el endpoint ya haya puesto su propia Cache-Control).
    ///  - la cadena Cache-Control a usar si la ruta SÍ está en la lista blanca.
    /// <paramref name="cacheable"/> indica si la respuesta es candidata a ETag/304 (lista blanca
    /// con contenido público/revalidable; no para private,no-store).
    /// </summary>
    public static string? ReglaPara(HttpRequest req, out bool cacheable)
    {
        cacheable = false;

        // Solo GET entra a la lista blanca; cualquier otro verbo cae al no-store por defecto.
        if (!HttpMethods.IsGet(req.Method))
            return null;

        // Normaliza el path sin query, sin barra final, en minúsculas.
        var path = (req.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();

        // --- Productos ---
        if (path == "/api/productos")
        {
            // EXCEPCIÓN CRÍTICA: el panel de la dueña pide inactivos -> nunca cache compartida.
            if (TraeIncluirInactivos(req))
                return PrivadoNoStore;
            cacheable = true;
            return Publico;
        }

        // Detalle por id: /api/productos/{id}. OJO: /api/productos/codigo/{codigo} NO se cachea
        // y /api/productos/imagenes/firmar es POST (ya filtrado por GET).
        if (path.StartsWith("/api/productos/"))
        {
            var resto = path["/api/productos/".Length..];
            // Un solo segmento = detalle por id. "codigo/..." tiene barra -> no entra.
            if (resto.Length > 0 && !resto.Contains('/'))
            {
                if (resto == "codigo") return null; // defensivo; "codigo" solo no es un detalle real
                cacheable = true;
                return Publico;
            }
            return null; // codigo/{codigo}, {id}/stock, etc.
        }

        // --- Catálogos auxiliares (listas públicas) ---
        if (path == "/api/categorias" || path == "/api/colores")
        {
            cacheable = true;
            return Publico;
        }

        // --- Config pública (siempre revalidar) ---
        if (path == "/api/config")
        {
            cacheable = true;
            return NoCache;
        }

        // Nada más se cachea (auth, apartados, pagos, avisos, clientas, personal, stock,
        // datos-bancarios, comprobantes, métricas, datos-bancarios de config, etc.).
        return null;
    }

    /// <summary>¿La query trae incluirInactivos=true? (case-insensitive).</summary>
    private static bool TraeIncluirInactivos(HttpRequest req)
        => req.Query.TryGetValue("incluirInactivos", out var v)
           && v.Any(s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ETag débil a partir del hash SHA-256 del cuerpo. Débil (<c>W/"..."</c>) porque solo garantiza
    /// equivalencia semántica, no byte a byte (la compresión puede cambiar los bytes en el cable).
    /// </summary>
    public static string CalcularETag(ReadOnlySpan<byte> cuerpo)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(cuerpo, hash);
        // 16 hex (8 bytes) basta para evitar colisiones prácticas y mantener la cabecera corta.
        return "W/\"" + Convert.ToHexString(hash[..8]).ToLowerInvariant() + "\"";
    }

    /// <summary>
    /// ¿El If-None-Match del cliente coincide con el ETag calculado? Acepta lista separada por comas
    /// y el comodín <c>*</c>. Compara ignorando el prefijo débil <c>W/</c> (comparación débil, que es
    /// la correcta para GET condicionales).
    /// </summary>
    public static bool CoincideIfNoneMatch(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch)) return false;
        var objetivo = Normalizar(etag);
        foreach (var parte in ifNoneMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (parte == "*") return true;
            if (Normalizar(parte) == objetivo) return true;
        }
        return false;
        static string Normalizar(string e)
        {
            e = e.Trim();
            if (e.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) e = e[2..].Trim();
            return e.Trim('"');
        }
    }
}
