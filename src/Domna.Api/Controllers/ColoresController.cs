using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// Catálogo de colores de la boutique. La lectura es pública (alta de prenda y filtro de la tienda);
/// el alta/edición/baja la hace la dueña. Borrar un color no afecta prendas (tienen su copia).
/// </summary>
[ApiController]
[Route("api/colores")]
public class ColoresController : ControllerBase
{
    private readonly IColorService _colores;

    public ColoresController(IColorService colores) => _colores = colores;

    /// <summary>Lista los colores del catálogo (con su familia calculada del hex).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ColorCatalogoDto>>> Listar(CancellationToken ct)
        => Ok(await _colores.ListarAsync(ct));

    /// <summary>Crea un color (nombre + hex). La familia se calcula sola.</summary>
    [HttpPost]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ColorCatalogoDto>> Crear(ColorInput input, CancellationToken ct)
        => Ok(await _colores.CrearAsync(input, ct));

    /// <summary>Edita el nombre y/o el hex de un color.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "duena")]
    public async Task<ActionResult<ColorCatalogoDto>> Editar(string id, ColorInput input, CancellationToken ct)
        => Ok(await _colores.EditarAsync(id, input, ct));

    /// <summary>Quita el color del catálogo (las prendas que lo usan conservan su copia).</summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "duena")]
    public async Task<IActionResult> Eliminar(string id, CancellationToken ct)
    {
        await _colores.EliminarAsync(id, ct);
        return NoContent();
    }
}
