using Domna.Infrastructure.Services;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Prueba el contrato de caché inmutable de las fotos de producto: la firma de subida debe devolver
/// la cabecera <c>Cache-Control: public, max-age=31536000, immutable</c> en <c>UrlFirmada.Cabeceras</c>
/// para que el front la reenvíe EXACTAMENTE igual en el PUT. Presignar no hace red (es solo cripto),
/// así que basta un R2Options con valores válidos de forma.
/// </summary>
public class R2CacheHeadersTests
{
    private static R2Almacenamiento Crear() => new(new R2Options
    {
        AccountId = "cuenta-demo",
        Bucket = "fotos",
        AccessKey = "AKIADEMO",
        SecretKey = "secretodemo",
        UrlPublica = "https://cdn.demo.test",
    });

    [Fact]
    public void FirmarSubidaProducto_devuelve_cache_control_inmutable_en_cada_tamano()
    {
        var r2 = Crear();
        var firma = r2.FirmarSubidaProducto("p-123", new[] { "400", "800", "1600" }, "image/webp");

        Assert.Equal(3, firma.Subidas.Count);
        foreach (var s in firma.Subidas)
        {
            Assert.True(s.Cabeceras.ContainsKey("Cache-Control"));
            Assert.Equal("public, max-age=31536000, immutable", s.Cabeceras["Cache-Control"]);
            // La ruta pública es versionada (inmutable) y la URL de subida está firmada.
            Assert.Contains("/productos/p-123/", s.UrlPublica);
            Assert.Contains($"/{s.Tamano}.webp", s.UrlPublica);
            Assert.False(string.IsNullOrWhiteSpace(s.UrlSubida));
        }
    }
}
