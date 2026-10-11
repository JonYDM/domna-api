using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// Catálogo de categorías (con sus tallas). La lectura es pública (la usa el wizard de alta y los
/// filtros de la tienda); el alta/edición/baja la hace la dueña.
/// </summary>
[ApiController]
[Route("api/categorias")]
public class CategoriasController : ControllerBase
{
    private readonly ICategoriaService _categorias;

    public CategoriasController(ICategoriaService categorias) => _categorias = categorias;

    /// <summary>Lista las categorías con sus tallas y cuántas prendas usan cada una.</summary>
    [HttpGet]
    public async Task<ActionResult<List<CategoriaDto>>> Listar(CancellationToken ct)
        => Ok(await _categorias.ListarAsync(ct));

    /// <summary>Crea una categoría con su juego de tallas.</summary>
    [HttpPost]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<CategoriaDto>> Crear(CategoriaInput input, CancellationToken ct)
        => Ok(await _categorias.CrearAsync(input, ct));

    /// <summary>Edita el nombre y las tallas (no afecta prendas ya dadas de alta).</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<CategoriaDto>> Editar(string id, CategoriaInput input, CancellationToken ct)
        => Ok(await _categorias.EditarAsync(id, input, ct));

    /// <summary>Borra la categoría. Falla (409) si tiene prendas asociadas.</summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "duena")]
    public async Task<IActionResult> Eliminar(string id, CancellationToken ct)
    {
        await _categorias.EliminarAsync(id, ct);
        return NoContent();
    }
}
