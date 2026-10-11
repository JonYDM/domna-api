using Amazon.S3;
using Amazon.S3.Model;
using Domna.Application.Abstractions;

namespace Domna.Infrastructure.Services;

/// <summary>
/// Opciones de Cloudflare R2 (sección "R2" / variables de entorno R2__*). Las llaves son secretas y
/// viven solo en el servidor. El endpoint S3 se arma con el AccountId.
/// </summary>
public class R2Options
{
    public string AccountId { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    /// <summary>URL pública del bucket (dominio r2.dev o propio) para construir las URLs finales.</summary>
    public string UrlPublica { get; set; } = "";
    public int MinutosVigenciaSubida { get; set; } = 10;

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(UrlPublica);

    public string Endpoint => $"https://{AccountId}.r2.cloudflarestorage.com";
}

/// <summary>
/// Almacenamiento en R2 usando el SDK de S3 (R2 es S3-compatible). Genera URLs PUT firmadas para que
/// el front suba los WebP directo al bucket, y devuelve la URL pública final de cada objeto.
/// </summary>
public class R2Almacenamiento : IAlmacenamientoImagenes
{
    private readonly R2Options _opt;
    private readonly IAmazonS3? _s3;

    public R2Almacenamiento(R2Options opt)
    {
        _opt = opt;
        if (opt.Configurado)
        {
            _s3 = new AmazonS3Client(opt.AccessKey, opt.SecretKey, new AmazonS3Config
            {
                ServiceURL = opt.Endpoint,
                // R2 requiere firma estilo path y la región "auto".
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            });
        }
    }

    public bool Disponible => _s3 is not null;

    public FirmaSubida FirmarSubidaProducto(string productoId, IEnumerable<string> tamanos, string contentType)
    {
        if (_s3 is null)
            throw new InvalidOperationException("R2 no está configurado.");

        var version = DateTime.UtcNow.Ticks.ToString("x"); // versiona para no pisar fotos anteriores
        var subidas = new List<UrlFirmada>();

        foreach (var t in tamanos)
        {
            var key = $"productos/{productoId}/{version}/{t}.webp";
            var req = new GetPreSignedUrlRequest
            {
                BucketName = _opt.Bucket,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(_opt.MinutosVigenciaSubida),
                ContentType = contentType,
            };
            var urlSubida = _s3.GetPreSignedURL(req);
            var urlPublica = $"{_opt.UrlPublica.TrimEnd('/')}/{key}";
            subidas.Add(new UrlFirmada(t, urlSubida, urlPublica));
        }

        return new FirmaSubida(subidas);
    }
}
