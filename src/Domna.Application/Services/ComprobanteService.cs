using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Comprobantes de transferencia. La clienta sube su imagen a una carpeta PRIVADA y la registra;
/// solo el personal puede verla, con su PIN y por un enlace que dura dos minutos. La llave del
/// archivo nunca viaja al cliente.
/// </summary>
public class ComprobanteService : IComprobanteService
{
    public const long MaxBytes = 8 * 1024 * 1024;
    public const int SegundosEnlace = 120;

    private static readonly string[] TiposPermitidos = { "image/jpeg", "image/png", "image/webp" };

    private readonly IApartadoRepository _apartados;
    private readonly IClientaRepository _clientas;
    private readonly IAlmacenamientoImagenes _almacen;
    private readonly IPasswordHasher _hasher;
    private readonly ILimitadorIntentos _limitador;
    private readonly IReloj _reloj;

    public ComprobanteService(
        IApartadoRepository apartados,
        IClientaRepository clientas,
        IAlmacenamientoImagenes almacen,
        IPasswordHasher hasher,
        ILimitadorIntentos limitador,
        IReloj reloj)
    {
        _apartados = apartados;
        _clientas = clientas;
        _almacen = almacen;
        _hasher = hasher;
        _limitador = limitador;
        _reloj = reloj;
    }

    // ── Clienta ──

    public async Task<ComprobanteFirmadoDto> FirmarSubidaAsync(string apartadoId, string contentType, string clientaId, CancellationToken ct = default)
    {
        ExigirAlmacen();
        var tipo = (contentType ?? "").Trim().ToLowerInvariant();
        if (!TiposPermitidos.Contains(tipo))
            throw DomnaException.BadRequest("Sube una foto del comprobante (JPG, PNG o WebP).");

        var a = await ObtenerDeLaClientaAsync(apartadoId, clientaId, ct);
        if (!a.AceptaComprobante)
            throw DomnaException.BadRequest("Este pedido no espera un comprobante de transferencia.");

        var f = _almacen.FirmarSubidaComprobante(a.Id, tipo);
        return new ComprobanteFirmadoDto(f.UrlSubida, f.Clave);
    }

    public async Task<ApartadoDto> RegistrarAsync(string apartadoId, string clave, string clientaId, CancellationToken ct = default)
    {
        ExigirAlmacen();
        var a = await ObtenerDeLaClientaAsync(apartadoId, clientaId, ct);

        // La llave debe ser de ESTE pedido: así nadie adjunta el archivo de otro.
        if (string.IsNullOrWhiteSpace(clave) || !clave.StartsWith($"comprobantes/{a.Id}/", StringComparison.Ordinal) || clave.Contains(".."))
            throw DomnaException.BadRequest("Comprobante inválido.");

        var info = await _almacen.ObtenerInfoComprobanteAsync(clave, ct)
            ?? throw DomnaException.BadRequest("No encontramos tu archivo. Vuelve a subirlo.");
        if (info.Bytes > MaxBytes)
            throw DomnaException.BadRequest("El archivo pesa demasiado (máximo 8 MB).");
        if (info.ContentType is null || !TiposPermitidos.Contains(info.ContentType.ToLowerInvariant()))
            throw DomnaException.BadRequest("Sube una foto del comprobante (JPG, PNG o WebP).");

        a.AdjuntarComprobante(clave, () => NuevoId("ab"), _reloj.Ahora);
        await _apartados.GuardarAsync(ct);
        return a.ToDto();
    }

    // ── Personal ──

    public async Task<ComprobanteVistaDto> VerAsync(string apartadoId, string abonoId, string pin, string usuarioId, CancellationToken ct = default)
    {
        ExigirAlmacen();
        var staff = await _clientas.ObtenerPorIdAsync(usuarioId, ct);
        if (staff is null || !staff.Activo || (staff.Rol != Rol.Duena && staff.Rol != Rol.Cajera))
            throw new DomnaException(403, "No tienes permiso para esto.");

        var a = await _apartados.ObtenerPorIdAsync(apartadoId, ct)
            ?? throw DomnaException.NotFound("Pedido no encontrado.");
        // Antes de gastar intentos de PIN: una cajera no abre comprobantes de otra sucursal.
        if (staff.Rol == Rol.Cajera && staff.SucursalCajera != a.Entrega)
            throw new DomnaException(403, "Ese pedido es de otra sucursal.");
        var abono = a.Abonos.FirstOrDefault(x => x.Id == abonoId && x.TieneComprobante)
            ?? throw DomnaException.NotFound("Ese pago no tiene comprobante.");

        var clave = $"pin-comprobante:{staff.Id}";
        if (_limitador.Bloqueo(clave) is { } espera)
            throw new DomnaException(429, $"Demasiados intentos. Vuelve a intentar en {Math.Max(1, (int)Math.Ceiling(espera.TotalMinutes))} min.");

        if (string.IsNullOrEmpty(staff.PinHash) || string.IsNullOrWhiteSpace(pin) || !_hasher.Verificar(pin.Trim(), staff.PinHash))
        {
            var quedan = _limitador.RegistrarFallo(clave);
            throw new DomnaException(403, quedan > 0
                ? $"PIN incorrecto. Te quedan {quedan} {(quedan == 1 ? "intento" : "intentos")}."
                : "Demasiados intentos. Vuelve a intentar en 10 min.");
        }
        _limitador.Reiniciar(clave);

        var url = _almacen.FirmarLecturaComprobante(abono.ComprobanteClave!, TimeSpan.FromSeconds(SegundosEnlace));
        return new ComprobanteVistaDto(url, SegundosEnlace);
    }

    // ── Apoyo ──

    private void ExigirAlmacen()
    {
        if (!_almacen.Disponible)
            throw new DomnaException(503, "El almacenamiento de comprobantes no está disponible por ahora.");
    }

    /// <summary>Un pedido ajeno responde igual que uno inexistente, para no revelar que existe.</summary>
    private async Task<Apartado> ObtenerDeLaClientaAsync(string apartadoId, string clientaId, CancellationToken ct)
    {
        var a = await _apartados.ObtenerPorIdAsync(apartadoId, ct);
        if (a is null || a.ClientaId != clientaId)
            throw DomnaException.NotFound("Pedido no encontrado.");
        return a;
    }

    private static string NuevoId(string prefijo) => $"{prefijo}-{Guid.NewGuid():N}"[..14];
}
