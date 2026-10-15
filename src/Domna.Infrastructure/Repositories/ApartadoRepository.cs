using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Repositories;

public class ApartadoRepository : IApartadoRepository
{
    private readonly DomnaDbContext _db;

    public ApartadoRepository(DomnaDbContext db) => _db = db;

    private IQueryable<Apartado> ConDetalle() =>
        _db.Apartados.Include(a => a.Lineas).Include(a => a.Abonos);

    public Task<Apartado?> ObtenerPorIdAsync(string id, CancellationToken ct = default) =>
        ConDetalle().FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<Apartado>> ListarAsync(CancellationToken ct = default) =>
        ConDetalle().OrderByDescending(a => a.Creado).ToListAsync(ct);

    public Task<List<Apartado>> ListarDeClientaAsync(string clientaId, CancellationToken ct = default) =>
        ConDetalle().Where(a => a.ClientaId == clientaId).OrderByDescending(a => a.Creado).ToListAsync(ct);

    // Las compras en línea por pagar reservan stock pero no son apartados: no cuentan para el límite.
    public Task<int> ContarActivosDeClientaAsync(string clientaId, CancellationToken ct = default) =>
        _db.Apartados.CountAsync(a => a.ClientaId == clientaId && a.Estado == EstadoApartado.Activo
            && a.Modalidad != ModalidadApartado.Compra, ct);

    public Task<List<Apartado>> ListarActivosVencidosAsync(DateTime hoy, CancellationToken ct = default) =>
        ConDetalle().Where(a => a.Estado == EstadoApartado.Activo && a.VenceEl <= hoy).ToListAsync(ct);

    public async Task<Dictionary<string, int>> ObtenerReservadoAsync(string varianteId, CancellationToken ct = default)
    {
        var filas = await _db.LineasApartado
            .Where(l => l.VarianteId == varianteId)
            .Join(_db.Apartados, l => l.ApartadoId, a => a.Id, (l, a) => new { l.SucursalOrigen, l.Cantidad, a.Estado })
            .Where(x => x.Estado == EstadoApartado.Activo)
            .ToListAsync(ct);

        return filas
            .GroupBy(x => x.SucursalOrigen)
            .ToDictionary(g => EnumMapSucursal(g.Key), g => g.Sum(x => x.Cantidad));
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> ObtenerReservadoPorVariantesAsync(
        IEnumerable<string> varianteIds, CancellationToken ct = default)
    {
        var ids = varianteIds.ToList();
        if (ids.Count == 0) return new();

        var filas = await _db.LineasApartado
            .Where(l => ids.Contains(l.VarianteId))
            .Join(_db.Apartados, l => l.ApartadoId, a => a.Id, (l, a) => new { l.VarianteId, l.SucursalOrigen, l.Cantidad, a.Estado })
            .Where(x => x.Estado == EstadoApartado.Activo)
            .ToListAsync(ct);

        return filas
            .GroupBy(x => x.VarianteId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.SucursalOrigen).ToDictionary(gg => EnumMapSucursal(gg.Key), gg => gg.Sum(x => x.Cantidad)));
    }

    public async Task AgregarAsync(Apartado apartado, CancellationToken ct = default) =>
        await _db.Apartados.AddAsync(apartado, ct);

    public Task GuardarAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<int> ObtenerMaxFolioAsync(CancellationToken ct = default)
    {
        // Los folios tienen el formato "DOM-NNNN". Traemos solo esa columna y sacamos el mayor N.
        // El catálogo de una boutique es chico; no vale la pena un SQL específico por proveedor.
        var folios = await _db.Apartados.Select(a => a.Folio).ToListAsync(ct);
        return MaxFolio(folios);
    }

    /// <summary>Extrae el mayor número de una lista de folios "DOM-NNNN" (0 si no hay ninguno válido).</summary>
    internal static int MaxFolio(IEnumerable<string> folios)
    {
        var max = 0;
        foreach (var f in folios)
        {
            if (string.IsNullOrEmpty(f)) continue;
            var guion = f.LastIndexOf('-');
            var parte = guion >= 0 ? f[(guion + 1)..] : f;
            if (int.TryParse(parte, out var n) && n > max) max = n;
        }
        return max;
    }

    public bool EsColisionDeFolio(Exception ex)
    {
        // Npgsql: violación de índice único => PostgresException con SqlState 23505, envuelta en
        // DbUpdateException. InMemory no valida índices únicos, así que allí no ocurre (el caso de
        // uso lo evita calculando el máximo + 1). Reconocemos la colisión sin acoplarnos a Npgsql.
        for (Exception? actual = ex; actual is not null; actual = actual.InnerException)
        {
            if (actual.GetType().Name == "PostgresException")
            {
                var sqlState = actual.GetType().GetProperty("SqlState")?.GetValue(actual) as string;
                if (sqlState == "23505" &&
                    (actual.Message.Contains("Folio", StringComparison.OrdinalIgnoreCase) ||
                     actual.Message.Contains("IX_Apartados_Folio", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        return false;
    }

    private static string EnumMapSucursal(SucursalId s) => s == SucursalId.Temixco ? "temixco" : "azteca";
}
