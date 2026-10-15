using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Equipo de la boutique: la dueña ve quién tiene acceso (ella + cajeras) y da de alta cajeras
/// nuevas con nombre, teléfono, PIN y sucursal. Reusa las reglas de Clienta.CrearStaff.
/// </summary>
public class PersonalService : IPersonalService
{
    private readonly IClientaRepository _clientas;
    private readonly IPasswordHasher _hasher;
    private readonly IReloj _reloj;
    private readonly ISesionService _sesiones;

    public PersonalService(IClientaRepository clientas, IPasswordHasher hasher, IReloj reloj, ISesionService sesiones)
    {
        _clientas = clientas;
        _hasher = hasher;
        _reloj = reloj;
        _sesiones = sesiones;
    }

    public async Task<List<ClientaDto>> ListarAsync(CancellationToken ct = default)
    {
        var staff = await _clientas.ListarStaffAsync(ct);
        return staff.Select(c => c.ToDto()).ToList();
    }

    public async Task<ClientaDto> CrearCajeraAsync(CrearCajeraInput input, CancellationToken ct = default)
    {
        var telefono = Normalizacion.NormalizarTelefono(input.Telefono);
        var pin = Normalizacion.NormalizarPin(input.Pin);
        var sucursal = EnumMap.Sucursal(input.Sucursal);

        if (await _clientas.ObtenerPorTelefonoAsync(telefono, ct) is not null)
            throw DomnaException.Conflict("Ya hay una cuenta con ese teléfono.");

        // Las cajeras no necesitan correo propio: se deriva del teléfono (canal interno, no se usa para avisos).
        var email = $"cajera.{telefono.Replace(" ", "")}@jeslyboutique.mx";

        var cajera = Clienta.CrearStaff(
            NuevoId(),
            input.Nombre,
            null,
            telefono,
            email,
            Rol.Cajera,
            _hasher.Hash(pin),
            _reloj.Ahora,
            sucursal);

        await _clientas.AgregarAsync(cajera, ct);
        await _clientas.GuardarAsync(ct);
        return cajera.ToDto();
    }

    public async Task<ClientaDto> EditarCajeraAsync(string id, EditarCajeraInput input, CancellationToken ct = default)
    {
        var cajera = await ObtenerCajeraAsync(id, ct);
        var telefono = Normalizacion.NormalizarTelefono(input.Telefono);
        var sucursal = EnumMap.Sucursal(input.Sucursal);

        var existente = await _clientas.ObtenerPorTelefonoAsync(telefono, ct);
        if (existente is not null && existente.Id != id)
            throw DomnaException.Conflict("Ya hay una cuenta con ese teléfono.");

        cajera.EditarStaff(input.Nombre, telefono, sucursal);
        await _clientas.GuardarAsync(ct);
        return cajera.ToDto();
    }

    public async Task<ClientaDto> ResetPinAsync(string id, ResetPinInput input, CancellationToken ct = default)
    {
        var cajera = await ObtenerCajeraAsync(id, ct);
        var pin = Normalizacion.NormalizarPin(input.Pin);
        cajera.FijarPinHash(_hasher.Hash(pin));
        await _clientas.GuardarAsync(ct);
        // Cambió el PIN de la cajera: se cierran todas sus sesiones (motivo "pin_cambiado").
        await _sesiones.RevocarTodasAsync(cajera.Id, MotivosRevocacion.PinCambiado, _reloj.Ahora, ct);
        return cajera.ToDto();
    }

    public async Task<ClientaDto> DesactivarAsync(string id, CancellationToken ct = default)
    {
        var cajera = await ObtenerCajeraAsync(id, ct);
        cajera.Desactivar();
        await _clientas.GuardarAsync(ct);
        // Se bloqueó el acceso: se cierran todas sus sesiones (motivo "desactivada").
        await _sesiones.RevocarTodasAsync(cajera.Id, MotivosRevocacion.Desactivada, _reloj.Ahora, ct);
        return cajera.ToDto();
    }

    public async Task<ClientaDto> ActivarAsync(string id, CancellationToken ct = default)
    {
        var cajera = await ObtenerCajeraAsync(id, ct);
        cajera.Activar();
        await _clientas.GuardarAsync(ct);
        return cajera.ToDto();
    }

    /// <summary>Busca una cajera por id. 404 si no existe; 400 si el id es de la dueña (no se edita aquí).</summary>
    private async Task<Clienta> ObtenerCajeraAsync(string id, CancellationToken ct)
    {
        var clienta = await _clientas.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("No encontramos a esa persona.");
        if (clienta.Rol != Rol.Cajera)
            throw DomnaException.BadRequest("Solo se puede editar a una cajera.");
        return clienta;
    }

    private static string NuevoId() => $"u-{Guid.NewGuid():N}"[..12];
}
