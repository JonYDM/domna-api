using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class AvisoRepository : IAvisoRepository
{
    private readonly DomnaDbContext _db;

    public AvisoRepository(DomnaDbContext db) => _db = db;

    public async Task<HashSet<string>> ObtenerLeidosAsync(string clientaId, CancellationToken ct = default)
    {
        var ids = await _db.AvisosLeidos.Where(x => x.ClientaId == clientaId).Select(x => x.AvisoId).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task MarcarLeidosAsync(string clientaId, IEnumerable<string> avisoIds, CancellationToken ct = default)
    {
        var yaLeidos = await ObtenerLeidosAsync(clientaId, ct);
        var nuevos = avisoIds.Where(id => !yaLeidos.Contains(id)).Select(id => new AvisoLeido(clientaId, id));
        await _db.AvisosLeidos.AddRangeAsync(nuevos, ct);
        await _db.SaveChangesAsync(ct);
    }
}
