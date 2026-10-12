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

    public PersonalService(IClientaRepository clientas, IPasswordHasher hasher, IReloj reloj)
    {
        _clientas = clientas;
        _hasher = hasher;
        _reloj = reloj;
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

    private static string NuevoId() => $"u-{Guid.NewGuid():N}"[..12];
}
