namespace Domna.Domain;

/// <summary>
/// Marca de "ya lo vi" de un aviso in-app. Los avisos mismos se DERIVAN del estado de los
/// apartados (no se guardan); solo se persiste qué ids ya leyó cada clienta.
/// </summary>
public class AvisoLeido
{
    public string ClientaId { get; private set; } = default!;
    public string AvisoId { get; private set; } = default!;

    private AvisoLeido() { }

    public AvisoLeido(string clientaId, string avisoId)
    {
        ClientaId = clientaId;
        AvisoId = avisoId;
    }
}
