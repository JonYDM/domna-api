using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>
/// CRUD de prenda. La lectura del catálogo es pública (como la demo); el alta/edición/baja y el
/// ajuste de stock requieren rol staff (dueña o cajera). Alineado con api.ts.
/// </summary>
[ApiController]
[Route("api/productos")]
public class ProductosController : ControllerBase
{
    private readonly IProductoService _productos;
    private readonly IAlmacenamientoImagenes _imagenes;

    public ProductosController(IProductoService productos, IAlmacenamientoImagenes imagenes)
    {
        _productos = productos;
        _imagenes = imagenes;
    }

    /// <summary>
    /// Firma la subida de las fotos de un producto a R2 (staff). El front sube los WebP directo al
    /// bucket con las URLs PUT devueltas y luego guarda las URLs públicas en el producto.
    /// </summary>
    [HttpPost("imagenes/firmar")]
    [Authorize(Roles = "duena,cajera")]
    public ActionResult<FirmaSubida> FirmarImagenes(FirmarImagenesInput input)
    {
        if (!_imagenes.Disponible)
            return StatusCode(503, new { mensaje = "El almacenamiento de imágenes no está configurado." });
        var tamanos = (input.Tamanos is { Count: > 0 }) ? input.Tamanos : new List<string> { "400", "800", "1600" };
        var firma = _imagenes.FirmarSubidaProducto(input.ProductoId, tamanos, input.ContentType ?? "image/webp");
        return Ok(firma);
    }


    /// <summary>Lista el catálogo. incluirInactivos=true solo para el panel de la dueña.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ProductoDto>>> Listar([FromQuery] bool incluirInactivos, CancellationToken ct)
        => Ok(await _productos.ListarAsync(incluirInactivos, ct));

    /// <summary>Detalle de una prenda por id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductoDto>> Obtener(string id, CancellationToken ct)
        => Ok(await _productos.ObtenerAsync(id, ct));

    /// <summary>Busca una prenda por el código de barras de alguna de sus variantes (POS).</summary>
    [HttpGet("codigo/{codigo}")]
    public async Task<ActionResult<ProductoDto>> PorCodigoBarras(string codigo, CancellationToken ct)
        => Ok(await _productos.BuscarPorCodigoBarrasAsync(codigo, ct));

    /// <summary>Alta de prenda: genera variantes talla × color con stock por sucursal y código de barras.</summary>
    [HttpPost]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ProductoDto>> Crear(NuevoProductoInput input, CancellationToken ct)
    {
        var creado = await _productos.CrearAsync(input, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Id }, creado);
    }

    /// <summary>Edita los campos simples de la prenda (nombre, descripción, precio, costo, oferta, apartable).</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ProductoDto>> Editar(string id, EditarProductoInput input, CancellationToken ct)
        => Ok(await _productos.EditarAsync(id, input, ct));

    /// <summary>Ajusta el stock absoluto de una variante en una sucursal.</summary>
    [HttpPost("{id}/stock")]
    [Authorize(Roles = "duena,cajera")]
    public async Task<ActionResult<ProductoDto>> AjustarStock(string id, AjusteStockInput input, CancellationToken ct)
        => Ok(await _productos.AjustarStockAsync(id, input, ct));

    /// <summary>Baja lógica de la prenda (deja de verse en el catálogo).</summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "duena")]
    public async Task<IActionResult> Desactivar(string id, CancellationToken ct)
    {
        await _productos.DesactivarAsync(id, ct);
        return NoContent();
    }
}
