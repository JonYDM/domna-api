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

    private static string EnumMapSucursal(SucursalId s) => s == SucursalId.Temixco ? "temixco" : "azteca";
}
