using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

/// <summary>
/// Persistencia EF de los registros de idempotencia. El índice único (UsuarioId, Ruta, Llave)
/// garantiza que, con varias instancias, solo una logre reservar la llave; las demás reintentan la
/// lectura y responden según el estado (en curso => 409, terminada => misma respuesta guardada).
/// </summary>
public class IdempotenciaRepository : IIdempotenciaRepository
{
    private readonly DomnaDbContext _db;

    public IdempotenciaRepository(DomnaDbContext db) => _db = db;

    public Task<Idempotencia?> ObtenerAsync(string usuarioId, string ruta, string llave, CancellationToken ct = default) =>
        _db.Idempotencias.FirstOrDefaultAsync(
            x => x.UsuarioId == usuarioId && x.Ruta == ruta && x.Llave == llave, ct);

    public async Task<bool> IntentarReservarAsync(Idempotencia registro, CancellationToken ct = default)
    {
        await _db.Idempotencias.AddAsync(registro, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (EsViolacionUnico(ex))
        {
            // Otra petición con la misma llave ganó la carrera: deja de rastrear este registro.
            _db.Entry(registro).State = EntityState.Detached;
            return false;
        }
    }

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public Task EliminarAsync(Idempotencia registro, CancellationToken ct = default)
    {
        _db.Idempotencias.Remove(registro);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<int> LimpiarVencidosAsync(DateTime limite, CancellationToken ct = default)
    {
        var vencidos = await _db.Idempotencias.Where(x => x.CreadoEl < limite).ToListAsync(ct);
        if (vencidos.Count == 0) return 0;
        _db.Idempotencias.RemoveRange(vencidos);
        await _db.SaveChangesAsync(ct);
        return vencidos.Count;
    }

    /// <summary>¿La excepción es una violación del índice único? (Npgsql: SqlState 23505.)</summary>
    private static bool EsViolacionUnico(Exception ex)
    {
        for (Exception? actual = ex; actual is not null; actual = actual.InnerException)
        {
            if (actual.GetType().Name == "PostgresException")
            {
                var sqlState = actual.GetType().GetProperty("SqlState")?.GetValue(actual) as string;
                if (sqlState == "23505") return true;
            }
        }
        return false;
    }
}
