using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class ColorRepository : IColorRepository
{
    private readonly DomnaDbContext _db;

    public ColorRepository(DomnaDbContext db) => _db = db;

    public Task<ColorCatalogo?> ObtenerPorIdAsync(string id, CancellationToken ct = default) =>
        _db.ColoresCatalogo.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<List<ColorCatalogo>> ListarAsync(CancellationToken ct = default) =>
        await _db.ColoresCatalogo.OrderBy(c => c.Nombre).ToListAsync(ct);

    public async Task<bool> ExisteNombreAsync(string nombre, string? exceptoId = null, CancellationToken ct = default)
    {
        var todos = await _db.ColoresCatalogo.ToListAsync(ct);
        return todos.Any(c => c.Id != exceptoId && Atributos.MismoNombre(c.Nombre, nombre));
    }

    public async Task AgregarAsync(ColorCatalogo color, CancellationToken ct = default) =>
        await _db.ColoresCatalogo.AddAsync(color, ct);

    public Task EliminarAsync(ColorCatalogo color, CancellationToken ct = default)
    {
        _db.ColoresCatalogo.Remove(color);
        return Task.CompletedTask;
    }

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
