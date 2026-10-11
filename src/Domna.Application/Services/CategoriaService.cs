using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// CRUD de categorías del catálogo. Reglas (portadas de atributos.ts): nombre único (ignorando
/// mayúsculas/acentos), tallas normalizadas (1–15), y no se puede borrar una categoría con prendas.
/// Editar no toca las prendas existentes.
/// </summary>
public class CategoriaService : ICategoriaService
{
    private readonly ICategoriaRepository _categorias;
    private readonly IProductoRepository _productos;

    public CategoriaService(ICategoriaRepository categorias, IProductoRepository productos)
    {
        _categorias = categorias;
        _productos = productos;
    }

    public async Task<List<CategoriaDto>> ListarAsync(CancellationToken ct = default)
    {
        var lista = await _categorias.ListarAsync(ct);
        var dtos = new List<CategoriaDto>();
        foreach (var c in lista)
            dtos.Add(c.ToDto(await _productos.ContarPorCategoriaAsync(c.Id, ct)));
        return dtos;
    }

    public async Task<CategoriaDto> CrearAsync(CategoriaInput input, CancellationToken ct = default)
    {
        var nombre = Atributos.LimpiarNombre(input.Nombre);
        if (await _categorias.ExisteNombreAsync(nombre, null, ct))
            throw DomnaException.Conflict("Ya existe esa categoría.");

        var id = await IdUnico(Atributos.Slug(nombre), ct);
        var categoria = Categoria.Crear(id, input.Nombre, input.Tallas);
        await _categorias.AgregarAsync(categoria, ct);
        await _categorias.GuardarAsync(ct);
        return categoria.ToDto(0);
    }

    public async Task<CategoriaDto> EditarAsync(string id, CategoriaInput input, CancellationToken ct = default)
    {
        var categoria = await _categorias.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Categoría no encontrada.");
        var nombre = Atributos.LimpiarNombre(input.Nombre);
        if (await _categorias.ExisteNombreAsync(nombre, id, ct))
            throw DomnaException.Conflict("Ya existe esa categoría.");

        categoria.Actualizar(input.Nombre, input.Tallas);
        await _categorias.GuardarAsync(ct);
        return categoria.ToDto(await _productos.ContarPorCategoriaAsync(id, ct));
    }

    public async Task EliminarAsync(string id, CancellationToken ct = default)
    {
        var categoria = await _categorias.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Categoría no encontrada.");
        var n = await _productos.ContarPorCategoriaAsync(id, ct);
        if (n > 0)
            throw DomnaException.Conflict(
                $"No se puede borrar: tiene {n} {(n == 1 ? "prenda" : "prendas")}. Muévelas a otra categoría primero.");

        await _categorias.EliminarAsync(categoria, ct);
        await _categorias.GuardarAsync(ct);
    }

    /// <summary>Genera un id a partir del slug, agregando sufijo si ya existe.</summary>
    private async Task<string> IdUnico(string baseId, CancellationToken ct)
    {
        var id = string.IsNullOrEmpty(baseId) ? "categoria" : baseId;
        var i = 2;
        while (await _categorias.ObtenerPorIdAsync(id, ct) is not null)
            id = $"{baseId}-{i++}";
        return id;
    }
}
