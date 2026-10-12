namespace Domna.Application.Dtos;

public record SucursalDto(string Id, string Nombre, string Direccion);

/// <summary>Configuración de negocio de la boutique. Equivale a ConfigBoutique del front.</summary>
public record ConfigBoutiqueDto(
    string Nombre,
    string Slug,
    int AnticipoPct,
    int VigenciaSinAnticipoDias,
    int VigenciaConAnticipoDias,
    decimal Penalizacion,
    int LimiteApartadosActivos,
    int DiasTraslado,
    bool Suspendida,
    int DiasNovedad,
    bool CatalogoPublico,
    List<SucursalDto> Sucursales);
