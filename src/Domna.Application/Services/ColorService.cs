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
    private readonly IProductoRepository _productos;

    public ColorService(IColorRepository colores, IProductoRepository productos)
    {
        _colores = colores;
        _productos = productos;
    }

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

    public async Task<ColorEditadoDto> EditarAsync(string id, ColorInput input, CancellationToken ct = default)
    {
        var color = await _colores.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Color no encontrado.");
        var nombre = Atributos.LimpiarNombre(input.Nombre, 2, 24);
        if (await _colores.ExisteNombreAsync(nombre, id, ct))
            throw DomnaException.Conflict("Ya existe un color con ese nombre.");

        // El nombre de ANTES: con él se reconocen las copias que usan las prendas.
        var nombreAnterior = color.Nombre;
        color.Actualizar(input.Nombre, input.Hex);

        // Es EL MISMO color: si cambia en el catálogo, cambia en todas las prendas que lo usan (se
        // reconocen por el nombre de antes). Cada prenda guarda su propia copia para que borrar un color
        // del catálogo no la rompa, pero editarlo sí debe verse en todas.
        var prendas = await _productos.ListarAsync(incluirInactivos: true, ct);
        var actualizadas = prendas.Count(p => p.ActualizarColor(nombreAnterior, color.Nombre, color.Hex, color.Familia));
        if (actualizadas > 0) await _productos.GuardarAsync(ct);

        await _colores.GuardarAsync(ct);
        return new ColorEditadoDto(color.Id, color.Nombre, color.Hex, EnumMap.Familia(color.Familia), actualizadas);
    }

    public async Task<UsoColorDto> UsoAsync(string id, CancellationToken ct = default)
    {
        var color = await _colores.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Color no encontrado.");
        var prendas = await _productos.ListarAsync(incluirInactivos: true, ct);
        return new UsoColorDto(prendas.Count(p => p.UsaColor(color.Nombre)));
    }

    public async Task<int> SincronizarPrendasAsync(CancellationToken ct = default)
    {
        // Pone cada prenda al día con el catálogo. Sirve para las que quedaron desfasadas cuando editar un
        // color todavía no llegaba a las prendas. Es idempotente: si ya están al día no cambia nada.
        var catalogo = await _colores.ListarAsync(ct);
        var prendas = await _productos.ListarAsync(incluirInactivos: true, ct);
        var cambios = 0;
        foreach (var p in prendas)
            foreach (var c in catalogo)
                if (p.ActualizarColor(c.Nombre, c.Nombre, c.Hex, c.Familia))
                    cambios++;
        if (cambios > 0) await _productos.GuardarAsync(ct);
        return cambios;
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
