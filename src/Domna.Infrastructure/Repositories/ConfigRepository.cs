using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

/// <summary>Config es una fila única; si no existe (primer arranque), se crea con defaults.</summary>
public class ConfigRepository : IConfigRepository
{
    private readonly DomnaDbContext _db;

    public ConfigRepository(DomnaDbContext db) => _db = db;

    public async Task<ConfigBoutique> ObtenerAsync(CancellationToken ct = default)
    {
        var c = await _db.Config.FirstOrDefaultAsync(ct);
        if (c is not null) return c;

        c = ConfigBoutique.CrearDefault("Jesly Boutique", "jesly-boutique");
        await _db.Config.AddAsync(c, ct);
        await _db.SaveChangesAsync(ct);
        return c;
    }

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
