namespace Domna.Application.Dtos;

/// <summary>
/// Fila del panel de Clientas (dueña): la clienta + datos derivados de sus pedidos.
/// El módulo de apartados/ventas todavía no existe en el backend (TODO: cuando se migre, calcular
/// aquí apartadosActivos/porRecoger/totalComprado/saldoPendiente/pedidos/ultimoPedido reales).
/// </summary>
public record ClientaResumenDto(
    string Id,
    string Nombre,
    string Apellidos,
    string Telefono,
    string Email,
    string Origen,
    DateTime Creada,
    DateTime? UltimoAcceso,
    decimal PenalizacionPendiente,
    int ApartadosActivos,
    int PorRecoger,
    decimal TotalComprado,
    decimal SaldoPendiente,
    int Pedidos,
    DateTime? UltimoPedido);

/// <summary>Resumen del panel de Clientas: totales y cómo llegaron.</summary>
public record ResumenClientasDto(
    int Total,
    int NuevasSemana,
    int ConApartadoActivo,
    int ConCargo,
    Dictionary<string, int> PorOrigen);

/// <summary>Alta de clienta en mostrador (la hace la dueña o una cajera). Nace sin PIN (sin acceso).</summary>
public record CrearClientaInput(string Nombre, string Apellidos, string Telefono, string Email);
