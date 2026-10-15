using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

/// <summary>
/// Persistencia EF de las sesiones. Índice por UsuarioId para listar rápido las sesiones de una
/// cuenta al validar la regla de "una sola sesión activa".
/// </summary>
public class SesionRepository : ISesionRepository
{
    private readonly DomnaDbContext _db;

    public SesionRepository(DomnaDbContext db) => _db = db;

    public Task<Sesion?> ObtenerPorIdAsync(string id, CancellationToken ct = default) =>
        _db.Sesiones.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<Sesion>> ListarNoRevocadasDeUsuarioAsync(string usuarioId, CancellationToken ct = default) =>
        _db.Sesiones.Where(x => x.UsuarioId == usuarioId && !x.Revocada).ToListAsync(ct);

    public async Task AgregarAsync(Sesion sesion, CancellationToken ct = default) =>
        await _db.Sesiones.AddAsync(sesion, ct);

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<int> LimpiarVencidasAsync(DateTime limite, CancellationToken ct = default)
    {
        var vencidas = await _db.Sesiones.Where(x => x.Expira < limite).ToListAsync(ct);
        if (vencidas.Count == 0) return 0;
        _db.Sesiones.RemoveRange(vencidas);
        await _db.SaveChangesAsync(ct);
        return vencidas.Count;
    }
}
