using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// CRUD del catálogo de colores. Reglas (portadas de atributos.ts): nombre único, hex normalizado
/// (#RGB→#RRGGBB) y familia calculada del hex. Borrar un color NO afecta prendas existentes (ya
/// tienen su copia del color).
/// </summary>
public class ColorService : IColorService
{
    private readonly IColorRepository _colores;

    public ColorService(IColorRepository colores) => _colores = colores;

    public async Task<List<ColorCatalogoDto>> ListarAsync(CancellationToken ct = default)
    {
        var lista = await _colores.ListarAsync(ct);
        return lista.Select(c => c.ToDto()).ToList();
    }

    public async Task<ColorCatalogoDto> CrearAsync(ColorInput input, CancellationToken ct = default)
    {
        var nombre = Atributos.LimpiarNombre(input.Nombre, 2, 24);
        if (await _colores.ExisteNombreAsync(nombre, null, ct))
            throw DomnaException.Conflict("Ya existe un color con ese nombre.");

        var id = await IdUnico(Atributos.Slug(nombre), ct);
        var color = ColorCatalogo.Crear(id, input.Nombre, input.Hex);
        await _colores.AgregarAsync(color, ct);
        await _colores.GuardarAsync(ct);
        return color.ToDto();
    }

    public async Task<ColorCatalogoDto> EditarAsync(string id, ColorInput input, CancellationToken ct = default)
    {
        var color = await _colores.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Color no encontrado.");
        var nombre = Atributos.LimpiarNombre(input.Nombre, 2, 24);
        if (await _colores.ExisteNombreAsync(nombre, id, ct))
            throw DomnaException.Conflict("Ya existe un color con ese nombre.");

        color.Actualizar(input.Nombre, input.Hex);
        await _colores.GuardarAsync(ct);
        return color.ToDto();
    }

    public async Task EliminarAsync(string id, CancellationToken ct = default)
    {
        var color = await _colores.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Color no encontrado.");
        await _colores.EliminarAsync(color, ct);
        await _colores.GuardarAsync(ct);
    }

    private async Task<string> IdUnico(string baseId, CancellationToken ct)
    {
        var id = string.IsNullOrEmpty(baseId) ? "color" : baseId;
        var i = 2;
        while (await _colores.ObtenerPorIdAsync(id, ct) is not null)
            id = $"{baseId}-{i++}";
        return id;
    }
}
