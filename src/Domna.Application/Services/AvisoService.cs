using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Avisos in-app de la clienta: se DERIVAN del estado de sus apartados (por vencer, vencido,
/// abonos, traslados, listos para recoger). No se persisten los avisos, solo qué ids ya se leyeron.
/// </summary>
public class AvisoService : IAvisoService
{
    private const int DiasAvisoVence = 3;

    private readonly IApartadoRepository _apartados;
    private readonly IAvisoRepository _avisosLeidos;
    private readonly IConfigRepository _config;
    private readonly IReloj _reloj;

    public AvisoService(IApartadoRepository apartados, IAvisoRepository avisosLeidos, IConfigRepository config, IReloj reloj)
    {
        _apartados = apartados;
        _avisosLeidos = avisosLeidos;
        _config = config;
        _reloj = reloj;
    }

    public async Task<List<AvisoDto>> ListarAsync(string clientaId, CancellationToken ct = default)
    {
        var hoy = _reloj.Ahora;
        var apartados = await _apartados.ListarDeClientaAsync(clientaId, ct);
        var leidos = await _avisosLeidos.ObtenerLeidosAsync(clientaId, ct);
        var config = await _config.ObtenerAsync(ct);

        var avisos = new List<(string Id, string Tipo, string Titulo, string Texto, DateTime Fecha, bool Urgente, string ApartadoId)>();

        foreach (var a in apartados)
        {
            if (a.Creado > hoy) continue;
            var prenda = a.Lineas.FirstOrDefault()?.Nombre ?? "tu prenda";
            var saldo = a.Saldo;

            if (a.Estado == EstadoApartado.Activo)
            {
                var dias = (int)Math.Round((a.VenceEl - hoy).TotalDays);
                if (dias <= DiasAvisoVence)
                {
                    var cuando = dias <= 0 ? "hoy" : dias == 1 ? "mañana" : $"en {dias} días";
                    var desde = a.VenceEl.AddDays(-DiasAvisoVence);
                    var fecha = desde > a.Creado ? desde : a.Creado;
                    avisos.Add((
                        $"por_vencer:{a.Id}", "por_vencer", $"Tu apartado vence {cuando}",
                        $"{prenda} · saldo {Mxn(saldo)}. Abona en tienda o por transferencia con tu folio {a.Folio}.",
                        fecha, true, a.Id));
                }
            }

            if (a.Estado == EstadoApartado.Vencido && a.CerradoEl is not null)
            {
                avisos.Add((
                    $"vencido:{a.Id}", "vencido", "Tu apartado venció",
                    $"{prenda} regresó a la tienda. Se sumará un cargo de {Mxn(config.Penalizacion)} a tu siguiente compra o apartado.",
                    a.CerradoEl.Value, false, a.Id));
            }

            // Pagos que la tienda ya confirmó (el anticipo o la compra los declaró la clienta y no se avisan aparte).
            foreach (var ab in a.Abonos)
            {
                if (ab.Estado != EstadoPago.Confirmado) continue;
                if (ab.Concepto != ConceptoAbono.Abono && ab.Concepto != ConceptoAbono.Liquidacion) continue;
                if (ab.Fecha > hoy) continue;
                var esLiquidacion = ab.Concepto == ConceptoAbono.Liquidacion;
                avisos.Add((
                    $"abono:{a.Id}:{ab.Id}", "abono",
                    esLiquidacion ? "¡Liquidaste tu apartado!" : $"Recibimos tu abono de {Mxn(ab.Monto)}",
                    esLiquidacion ? $"{prenda} ya es tuya. Folio {a.Folio}." : $"{prenda} · te quedan {Mxn(Math.Max(0, saldo))} por pagar.",
                    ab.Fecha, false, a.Id));
            }

            if (a.EstadoEntrega == EstadoEntrega.EnTraslado && a.Estado != EstadoApartado.Cancelado && a.Estado != EstadoApartado.Vencido)
            {
                avisos.Add((
                    $"traslado:{a.Id}", "traslado", $"Tu prenda va en camino a {NombreSucursal(a.Entrega)}",
                    $"{prenda} · llega aprox. el {a.ListoEstimado:dd/MM/yyyy}.",
                    a.Creado, false, a.Id));
            }

            if (a.Estado == EstadoApartado.Liquidado && a.EstadoEntrega == EstadoEntrega.Listo)
            {
                var fecha = new[] { a.CerradoEl ?? a.Creado, a.ListoEstimado }.Max();
                avisos.Add((
                    $"lista:{a.Id}", "lista", "¡Tu prenda está lista para recoger!",
                    $"{prenda} te espera en {NombreSucursal(a.Entrega)}. Muestra tu folio {a.Folio}.",
                    fecha, false, a.Id));
            }
        }

        return avisos
            .Where(x => x.Fecha <= hoy)
            .Select(x => new AvisoDto(x.Id, x.Tipo, x.Titulo, x.Texto, x.Fecha, leidos.Contains(x.Id), x.Urgente, x.ApartadoId))
            .OrderByDescending(x => x.Urgente && !x.Leido)
            .ThenByDescending(x => x.Fecha)
            .ToList();
    }

    public Task MarcarLeidosAsync(string clientaId, List<string> avisoIds, CancellationToken ct = default) =>
        _avisosLeidos.MarcarLeidosAsync(clientaId, avisoIds, ct);

    private static string NombreSucursal(SucursalId s) => s == SucursalId.Temixco ? "Temixco" : "La Azteca";
    private static string Mxn(decimal v) => v.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("es-MX"));
}
