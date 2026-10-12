using Domna.Application.Dtos;
using Domna.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domna.Api.Controllers;

/// <summary>Movimientos de inventario con historial. Solo staff (dueña o cajera).</summary>
[ApiController]
[Route("api/stock")]
[Authorize(Roles = "duena,cajera")]
public class StockController : ControllerBase
{
    private readonly IStockService _stock;

    public StockController(IStockService stock) => _stock = stock;

    /// <summary>Guarda varios cambios de stock de una prenda (valores absolutos), todo o nada, con motivo.</summary>
    [HttpPost("{productoId}/lote")]
    public async Task<ActionResult<GuardarStockResultado>> GuardarLote(string productoId, GuardarStockInput input, CancellationToken ct)
        => Ok(await _stock.GuardarLoteAsync(productoId, input, ct));

    /// <summary>Mueve piezas disponibles de una sucursal a la otra.</summary>
    [HttpPost("mover")]
    public async Task<IActionResult> Mover(MoverStockInput input, CancellationToken ct)
    {
        await _stock.MoverAsync(input, ct);
        return NoContent();
    }
}
