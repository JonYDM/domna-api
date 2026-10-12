using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class MovimientoRepository : IMovimientoRepository
{
    private readonly DomnaDbContext _db;

    public MovimientoRepository(DomnaDbContext db) => _db = db;

    // No guarda: el movimiento se persiste en la misma transacción que el cambio de stock.
    public async Task AgregarAsync(MovimientoInventario movimiento, CancellationToken ct = default) =>
        await _db.Movimientos.AddAsync(movimiento, ct);

    public Task<List<MovimientoInventario>> ListarDeProductoAsync(string productoId, CancellationToken ct = default) =>
        _db.Movimientos.Where(m => m.ProductoId == productoId).OrderByDescending(m => m.Fecha).ToListAsync(ct);
}
