using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class CategoriaRepository : ICategoriaRepository
{
    private readonly DomnaDbContext _db;

    public CategoriaRepository(DomnaDbContext db) => _db = db;

    public Task<Categoria?> ObtenerPorIdAsync(string id, CancellationToken ct = default) =>
        _db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<List<Categoria>> ListarAsync(CancellationToken ct = default) =>
        await _db.Categorias.OrderBy(c => c.Nombre).ToListAsync(ct);

    public async Task<bool> ExisteNombreAsync(string nombre, string? exceptoId = null, CancellationToken ct = default)
    {
        // La comparación por slug (ignora mayúsculas/acentos) se hace en memoria.
        var todas = await _db.Categorias.ToListAsync(ct);
        return todas.Any(c => c.Id != exceptoId && Atributos.MismoNombre(c.Nombre, nombre));
    }

    public async Task AgregarAsync(Categoria categoria, CancellationToken ct = default) =>
        await _db.Categorias.AddAsync(categoria, ct);

    public Task EliminarAsync(Categoria categoria, CancellationToken ct = default)
    {
        _db.Categorias.Remove(categoria);
        return Task.CompletedTask;
    }

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
