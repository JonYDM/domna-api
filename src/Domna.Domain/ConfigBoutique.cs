namespace Domna.Domain;

/// <summary>
/// Configuración de negocio de la boutique (fila única). Reglas de apartados, novedades y el
/// kill switch del SaaS. Equivale a la interfaz ConfigBoutique de src/types/api.ts.
/// </summary>
public class ConfigBoutique
{
    public string Id { get; private set; } = "config";
    public string Nombre { get; private set; } = default!;
    public string Slug { get; private set; } = default!;

    /// <summary>% de anticipo de la modalidad "con anticipo".</summary>
    public int AnticipoPct { get; private set; } = 30;
    public int VigenciaSinAnticipoDias { get; private set; } = 2;
    public int VigenciaConAnticipoDias { get; private set; } = 7;

    /// <summary>Cargo por dejar vencer un apartado (se suma al siguiente).</summary>
    public decimal Penalizacion { get; private set; } = 100m;
    public int LimiteApartadosActivos { get; private set; } = 3;

    /// <summary>Días hábiles de traslado entre sucursales.</summary>
    public int DiasTraslado { get; private set; } = 2;

    /// <summary>Kill switch del SaaS: si la boutique no pagó la renta.</summary>
    public bool Suspendida { get; private set; }

    /// <summary>Días que una prenda se muestra como "Nuevo" o "De vuelta".</summary>
    public int DiasNovedad { get; private set; } = 7;

    /// <summary>true: cualquiera ve catálogo y precios sin cuenta. false: solo con cuenta.</summary>
    public bool CatalogoPublico { get; private set; } = true;

    private ConfigBoutique() { }

    /// <summary>A quién se le transfiere (nombre de la cuenta). Null = la boutique aún no los captura.</summary>
    public string? BancoTitular { get; private set; }
    public string? BancoNombre { get; private set; }

    /// <summary>CLABE de 18 dígitos, sin espacios.</summary>
    public string? BancoClabe { get; private set; }

    public bool TieneDatosBancarios =>
        !string.IsNullOrWhiteSpace(BancoTitular) && !string.IsNullOrWhiteSpace(BancoNombre) && !string.IsNullOrWhiteSpace(BancoClabe);

    /// <summary>Fija los datos para recibir transferencias. Los tres vacíos los borran.</summary>
    public void FijarDatosBancarios(string? titular, string? banco, string? clabe)
    {
        var t = Normalizacion.NormalizarNombre(titular);
        var b = Normalizacion.NormalizarNombre(banco);
        var c = Clabe.Limpiar(clabe);

        if (t.Length == 0 && b.Length == 0 && c.Length == 0)
        {
            BancoTitular = BancoNombre = BancoClabe = null;
            return;
        }
        if (t.Length < 2) throw DomnaException.BadRequest("Escribe el nombre del titular de la cuenta.");
        if (b.Length < 2) throw DomnaException.BadRequest("Escribe el nombre del banco.");
        if (!Clabe.EsValida(c)) throw DomnaException.BadRequest("La CLABE no es válida: debe tener 18 dígitos.");

        BancoTitular = t;
        BancoNombre = b;
        BancoClabe = c;
    }

    public static ConfigBoutique CrearDefault(string nombre, string slug) => new()
    {
        Nombre = nombre,
        Slug = slug,
    };

    public void FijarSuspendida(bool suspendida) => Suspendida = suspendida;
    public void FijarCatalogoPublico(bool publico) => CatalogoPublico = publico;
}
