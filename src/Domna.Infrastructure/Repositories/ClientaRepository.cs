using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class ClientaRepository : IClientaRepository
{
    private readonly DomnaDbContext _db;

    public ClientaRepository(DomnaDbContext db) => _db = db;

    public Task<Clienta?> ObtenerPorIdAsync(string id, CancellationToken ct = default) =>
        _db.Clientas.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Clienta?> ObtenerPorTelefonoAsync(string telefono, CancellationToken ct = default) =>
        _db.Clientas.FirstOrDefaultAsync(c => c.Telefono == telefono, ct);

    public Task<Clienta?> ObtenerPorEmailAsync(string email, CancellationToken ct = default) =>
        _db.Clientas.FirstOrDefaultAsync(c => c.Email == email, ct);

    public Task<List<Clienta>> ListarStaffAsync(CancellationToken ct = default) =>
        _db.Clientas
            .Where(c => c.Rol != Rol.Clienta)
            .OrderBy(c => c.Rol)
            .ThenBy(c => c.Nombre)
            .ToListAsync(ct);

    public Task<List<Clienta>> ListarClientasAsync(CancellationToken ct = default) =>
        _db.Clientas
            .Where(c => c.Rol == Rol.Clienta)
            .OrderByDescending(c => c.Creada)
            .ToListAsync(ct);

    public async Task AgregarAsync(Clienta clienta, CancellationToken ct = default) =>
        await _db.Clientas.AddAsync(clienta, ct);

    public async Task AgregarRecuperacionAsync(RecuperacionPin recuperacion, CancellationToken ct = default) =>
        await _db.Recuperaciones.AddAsync(recuperacion, ct);

    public Task<RecuperacionPin?> ObtenerRecuperacionAsync(string token, CancellationToken ct = default) =>
        _db.Recuperaciones.FirstOrDefaultAsync(r => r.Token == token, ct);

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
