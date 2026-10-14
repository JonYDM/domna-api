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


/// <summary>Cuenta donde la boutique recibe transferencias. <c>Configurados</c> = false si aún no la captura.</summary>
public record DatosBancariosDto(string Titular, string Banco, string Clabe, bool Configurados);

/// <summary>Los tres vacíos borran los datos.</summary>
public record DatosBancariosInput(string? Titular, string? Banco, string? Clabe);
