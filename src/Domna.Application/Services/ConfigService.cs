using Domna.Application.Abstractions;
using Domna.Application.Dtos;

namespace Domna.Application.Services;

/// <summary>Configuración de negocio: la fila única con las reglas de apartados y el kill switch del SaaS.</summary>
public class ConfigService : IConfigService
{
    private static readonly List<SucursalDto> Sucursales = new()
    {
        new("temixco", "Boutique Temixco", "Av. Emiliano Zapata 120, Temixco, Mor."),
        new("azteca", "Boutique La Azteca", "Calle Azteca 45, Col. La Azteca, Temixco, Mor."),
    };

    private readonly IConfigRepository _config;

    public ConfigService(IConfigRepository config) => _config = config;

    public async Task<ConfigBoutiqueDto> ObtenerAsync(CancellationToken ct = default) =>
        ToDto(await _config.ObtenerAsync(ct));

    public async Task<ConfigBoutiqueDto> FijarSuspendidaAsync(bool suspendida, CancellationToken ct = default)
    {
        var c = await _config.ObtenerAsync(ct);
        c.FijarSuspendida(suspendida);
        await _config.GuardarAsync(ct);
        return ToDto(c);
    }

    public async Task<ConfigBoutiqueDto> FijarCatalogoPublicoAsync(bool publico, CancellationToken ct = default)
    {
        var c = await _config.ObtenerAsync(ct);
        c.FijarCatalogoPublico(publico);
        await _config.GuardarAsync(ct);
        return ToDto(c);
    }

    public async Task<DatosBancariosDto> ObtenerDatosBancariosAsync(CancellationToken ct = default) =>
        DatosDe(await _config.ObtenerAsync(ct));

    public async Task<DatosBancariosDto> FijarDatosBancariosAsync(DatosBancariosInput input, CancellationToken ct = default)
    {
        var c = await _config.ObtenerAsync(ct);
        c.FijarDatosBancarios(input.Titular, input.Banco, input.Clabe);
        await _config.GuardarAsync(ct);
        return DatosDe(c);
    }

    private static DatosBancariosDto DatosDe(Domain.ConfigBoutique c) =>
        new(c.BancoTitular ?? "", c.BancoNombre ?? "", c.BancoClabe ?? "", c.TieneDatosBancarios);

    private static ConfigBoutiqueDto ToDto(Domain.ConfigBoutique c) => new(
        c.Nombre, c.Slug, c.AnticipoPct, c.VigenciaSinAnticipoDias, c.VigenciaConAnticipoDias,
        c.Penalizacion, c.LimiteApartadosActivos, c.DiasTraslado, c.Suspendida, c.DiasNovedad,
        c.CatalogoPublico, Sucursales);
}
