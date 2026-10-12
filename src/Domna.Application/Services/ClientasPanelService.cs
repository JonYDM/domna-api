using Domna.Application.Abstractions;
using Domna.Application.Dtos;
using Domna.Application.Mapping;
using Domna.Domain;

namespace Domna.Application.Services;

/// <summary>
/// Panel de Clientas de la dueña: lista, resumen y ficha. El módulo de apartados/ventas aún no
/// existe en el backend, así que las métricas derivadas de pedidos (apartados activos, comprado,
/// saldo pendiente, etc.) quedan en cero por ahora — se calcularán reales cuando ese módulo exista.
/// </summary>
public class ClientasPanelService : IClientasPanelService
{
    private const int DiasClientaNueva = 7;

    private readonly IClientaRepository _clientas;
    private readonly IReloj _reloj;

    public ClientasPanelService(IClientaRepository clientas, IReloj reloj)
    {
        _clientas = clientas;
        _reloj = reloj;
    }

    public async Task<List<ClientaResumenDto>> ListarAsync(string filtro, string? texto, CancellationToken ct = default)
    {
        var lista = await _clientas.ListarClientasAsync(ct);
        var ahora = _reloj.Ahora;

        var filtrada = lista.Where(c => CoincideFiltro(c, filtro, ahora));

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            var digitos = new string(t.Where(char.IsDigit).ToArray());
            filtrada = filtrada.Where(c =>
                c.Nombre.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                c.Apellidos.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                c.Email.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                (digitos.Length >= 3 && c.Telefono.Replace(" ", "").Contains(digitos)));
        }

        return filtrada.Select(ToResumenDto).ToList();
    }

    public async Task<ResumenClientasDto> ResumenAsync(CancellationToken ct = default)
    {
        var lista = await _clientas.ListarClientasAsync(ct);
        var ahora = _reloj.Ahora;

        var porOrigen = new Dictionary<string, int> { ["registro"] = 0, ["mostrador"] = 0 };
        var nuevasSemana = 0;
        var conCargo = 0;
        foreach (var c in lista)
        {
            if (EsNueva(c, ahora)) nuevasSemana++;
            if (c.PenalizacionPendiente > 0) conCargo++;
            var origen = EnumMap.Origen(c.Origen);
            porOrigen[origen] = porOrigen.GetValueOrDefault(origen) + 1;
        }

        // TODO(apartados): conApartadoActivo requiere el módulo de apartados; hoy no existe ⇒ 0.
        return new ResumenClientasDto(lista.Count, nuevasSemana, 0, conCargo, porOrigen);
    }

    public async Task<ClientaResumenDto> ObtenerAsync(string id, CancellationToken ct = default)
    {
        var c = await _clientas.ObtenerPorIdAsync(id, ct)
            ?? throw DomnaException.NotFound("Clienta no encontrada.");
        if (c.Rol != Rol.Clienta)
            throw DomnaException.NotFound("Clienta no encontrada.");
        return ToResumenDto(c);
    }

    public async Task<ClientaDto> CrearAsync(CrearClientaInput input, CancellationToken ct = default)
    {
        var telefono = Normalizacion.NormalizarTelefono(input.Telefono);
        var email = Normalizacion.NormalizarEmail(input.Email);

        if (await _clientas.ObtenerPorTelefonoAsync(telefono, ct) is not null)
            throw DomnaException.Conflict("Ya hay una clienta con ese teléfono.");
        if (await _clientas.ObtenerPorEmailAsync(email, ct) is not null)
            throw DomnaException.Conflict("Ya hay una clienta con ese correo.");

        // Nace sin PIN (sin acceso): si luego se registra con este mismo teléfono, reclama la ficha.
        var clienta = Clienta.Crear(
            NuevoId(),
            input.Nombre,
            input.Apellidos,
            telefono,
            email,
            OrigenClienta.Mostrador,
            _reloj.Ahora);

        await _clientas.AgregarAsync(clienta, ct);
        await _clientas.GuardarAsync(ct);
        return clienta.ToDto();
    }

    private static string NuevoId() => $"c-{Guid.NewGuid():N}"[..12];

    private bool CoincideFiltro(Clienta c, string filtro, DateTime ahora) => filtro switch
    {
        "nuevas" => EsNueva(c, ahora),
        "cargo" => c.PenalizacionPendiente > 0,
        // TODO(apartados): "activas" (con apartado activo) requiere el módulo de apartados; hoy no hay ninguna.
        "activas" => false,
        _ => true,
    };

    private static bool EsNueva(Clienta c, DateTime ahora) => (ahora - c.Creada).TotalDays < DiasClientaNueva;

    /// <summary>
    /// TODO(apartados): apartadosActivos/porRecoger/totalComprado/saldoPendiente/pedidos/ultimoPedido
    /// deberán calcularse de los apartados reales de la clienta cuando ese módulo exista en el backend.
    /// </summary>
    private static ClientaResumenDto ToResumenDto(Clienta c) => new(
        c.Id,
        c.Nombre,
        c.Apellidos,
        c.Telefono,
        c.Email,
        EnumMap.Origen(c.Origen),
        c.Creada,
        c.UltimoAcceso,
        c.PenalizacionPendiente,
        ApartadosActivos: 0,
        PorRecoger: 0,
        TotalComprado: 0,
        SaldoPendiente: 0,
        Pedidos: 0,
        UltimoPedido: null);
}
