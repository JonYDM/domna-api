namespace Domna.Application.Abstractions;

/// <summary>
/// Una subida firmada: a dónde hace PUT el front, la URL pública final del objeto y las cabeceras
/// que el cliente DEBE enviar EXACTAMENTE igual en el PUT (contrato con la firma). Para las fotos de
/// producto incluye <c>Cache-Control: public, max-age=31536000, immutable</c> (rutas versionadas).
/// Si la firma no fija cabeceras, el diccionario va vacío.
/// </summary>
public record UrlFirmada(
    string Tamano,
    string UrlSubida,
    string UrlPublica,
    IReadOnlyDictionary<string, string> Cabeceras);

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

    /// <summary>
    /// Firma la subida de un comprobante de transferencia (imagen). Va a una carpeta aparte,
    /// <c>comprobantes/{apartadoId}/</c>, con nombre aleatorio. Nunca se devuelve una URL pública.
    /// </summary>
    ComprobanteFirmado FirmarSubidaComprobante(string apartadoId, string contentType);

    /// <summary>Tamaño y tipo del objeto subido, o null si no existe (para validar antes de aceptarlo).</summary>
    Task<InfoObjeto?> ObtenerInfoComprobanteAsync(string clave, CancellationToken ct = default);

    /// <summary>Enlace de lectura temporal de un comprobante. Es la única forma de verlo.</summary>
    string FirmarLecturaComprobante(string clave, TimeSpan vigencia);
}

/// <summary>A dónde sube la clienta su comprobante y la llave privada con la que queda guardado.</summary>
public record ComprobanteFirmado(string UrlSubida, string Clave);

/// <summary>Lo que se sabe de un objeto ya guardado.</summary>
public record InfoObjeto(long Bytes, string? ContentType);

/// <summary>Cuenta intentos fallidos (p. ej. de PIN) y bloquea temporalmente al pasarse del límite.</summary>
public interface ILimitadorIntentos
{
    /// <summary>Cuánto falta para poder reintentar, o null si no está bloqueado.</summary>
    TimeSpan? Bloqueo(string clave);

    /// <summary>Anota un fallo. Devuelve cuántos intentos quedan antes del bloqueo (0 = ya bloqueado).</summary>
    int RegistrarFallo(string clave);

    /// <summary>Borra el historial de fallos (tras un acierto).</summary>
    void Reiniciar(string clave);
}
