namespace Domna.Application.Abstractions;

/// <summary>Una subida firmada: a dónde hace PUT el front y la URL pública final del objeto.</summary>
public record UrlFirmada(string Tamano, string UrlSubida, string UrlPublica);

/// <summary>Resultado de firmar la subida de las fotos de un producto.</summary>
public record FirmaSubida(IReadOnlyList<UrlFirmada> Subidas);

/// <summary>
/// Almacenamiento de imágenes (Cloudflare R2 vía S3). Genera URLs firmadas de subida (PUT) para que
/// el front suba los WebP directo al bucket; las llaves nunca salen del servidor. Devuelve además la
/// URL pública final de cada objeto (para guardarla en el producto).
/// </summary>
public interface IAlmacenamientoImagenes
{
    /// <summary>¿Está configurado R2? (si no, el endpoint responde 503 con mensaje claro).</summary>
    bool Disponible { get; }

    /// <summary>
    /// Firma la subida de los tamaños pedidos (p. ej. "400","800","1600") para un producto.
    /// contentType suele ser "image/webp". Las URLs de subida expiran en minutos.
    /// </summary>
    FirmaSubida FirmarSubidaProducto(string productoId, IEnumerable<string> tamanos, string contentType);
}
