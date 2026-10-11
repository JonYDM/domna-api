using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class ProductoRepository : IProductoRepository
{
    private readonly DomnaDbContext _db;

    public ProductoRepository(DomnaDbContext db) => _db = db;

    public Task<Producto?> ObtenerPorIdAsync(string id, CancellationToken ct = default) =>
        _db.Productos
            .Include(p => p.Colores)
            .Include(p => p.Variantes)
            .Include(p => p.Imagenes)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<List<Producto>> ListarAsync(bool incluirInactivos, CancellationToken ct = default)
    {
        var q = _db.Productos
            .Include(p => p.Colores)
            .Include(p => p.Variantes)
            .Include(p => p.Imagenes)
            .AsQueryable();
        if (!incluirInactivos)
            q = q.Where(p => p.Activo);
        return await q.OrderByDescending(p => p.Creado).ToListAsync(ct);
    }

    public async Task<Producto?> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default)
    {
        // El código vive por variante; devolvemos el producto que contiene esa variante.
        var variante = await _db.Variantes.FirstOrDefaultAsync(v => v.CodigoBarras == codigo, ct);
        if (variante is null) return null;
        return await ObtenerPorIdAsync(variante.ProductoId, ct);
    }

    public async Task AgregarAsync(Producto producto, CancellationToken ct = default) =>
        await _db.Productos.AddAsync(producto, ct);

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public Task<int> ContarPorCategoriaAsync(string categoriaId, CancellationToken ct = default) =>
        _db.Productos.CountAsync(p => p.CategoriaId == categoriaId, ct);
}
