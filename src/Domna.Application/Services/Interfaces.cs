using Domna.Application.Dtos;

namespace Domna.Application.Services;

/// <summary>Casos de uso de autenticación de la clienta.</summary>
public interface IAuthService
{
    Task<SesionDto> RegistrarAsync(RegistroClientaInput input, CancellationToken ct = default);
    Task<SesionDto> LoginAsync(LoginInput input, CancellationToken ct = default);

    /// <summary>¿El teléfono ya tiene cuenta? Solo bool, sin revelar nombre (anti-enumeración mínima).</summary>
    Task<bool> ExisteTelefonoAsync(string telefono, CancellationToken ct = default);
    Task<RecuperacionDto> SolicitarRecuperacionAsync(SolicitarRecuperacionInput input, CancellationToken ct = default);
    Task<SesionDto> ConfirmarRecuperacionAsync(ConfirmarRecuperacionInput input, CancellationToken ct = default);
}

/// <summary>Casos de uso del equipo (dueña y cajeras) para el panel de Personal.</summary>
public interface IPersonalService
{
    Task<List<ClientaDto>> ListarAsync(CancellationToken ct = default);
    Task<ClientaDto> CrearCajeraAsync(CrearCajeraInput input, CancellationToken ct = default);

    /// <summary>Edita nombre, teléfono y sucursal de una cajera. Solo aplica a cajeras, no a la dueña.</summary>
    Task<ClientaDto> EditarCajeraAsync(string id, EditarCajeraInput input, CancellationToken ct = default);

    /// <summary>Fija un PIN nuevo para una cajera que lo olvidó.</summary>
    Task<ClientaDto> ResetPinAsync(string id, ResetPinInput input, CancellationToken ct = default);

    /// <summary>Bloquea el acceso de una cajera (conserva su historial).</summary>
    Task<ClientaDto> DesactivarAsync(string id, CancellationToken ct = default);

    /// <summary>Restaura el acceso de una cajera desactivada.</summary>
    Task<ClientaDto> ActivarAsync(string id, CancellationToken ct = default);
}

/// <summary>Casos de uso del panel de Clientas (dueña): listar, resumen y ficha.</summary>
public interface IClientasPanelService
{
    Task<List<ClientaResumenDto>> ListarAsync(string filtro, string? texto, CancellationToken ct = default);
    Task<ResumenClientasDto> ResumenAsync(CancellationToken ct = default);
    Task<ClientaResumenDto> ObtenerAsync(string id, CancellationToken ct = default);

    /// <summary>Alta en mostrador: la clienta nace sin PIN (sin acceso) hasta que se registre.</summary>
    Task<ClientaDto> CrearAsync(CrearClientaInput input, CancellationToken ct = default);
}

/// <summary>Casos de uso de apartados/ventas: cotizar, crear, abonar, cancelar, avanzar entrega.</summary>
public interface IApartadoService
{
    Task<CotizacionApartadoDto> CotizarAsync(string? clientaId, CrearApartadoInput input, CancellationToken ct = default);
    Task<ApartadoDto> CrearAsync(string? clientaId, CrearApartadoInput input, CancellationToken ct = default);
    Task<ApartadoDto> AbonarAsync(RegistrarAbonoInput input, CancellationToken ct = default);
    Task<ApartadoDto> CancelarAsync(string id, CancellationToken ct = default);
    Task<ApartadoDto> AvanzarEntregaAsync(string id, string siguiente, CancellationToken ct = default);
    Task<List<ApartadoDto>> ListarAsync(string? estado, string? texto, string? tipo, CancellationToken ct = default);
    Task<ApartadoDto> ObtenerAsync(string id, CancellationToken ct = default);
    Task<List<ApartadoDto>> ListarDeClientaAsync(string clientaId, CancellationToken ct = default);

    /// <summary>Pedidos con un pago declarado en línea que la tienda aún no confirma. La cajera ve solo los de su sucursal.</summary>
    Task<List<ApartadoDto>> ListarPorConfirmarAsync(string usuarioId, CancellationToken ct = default);

    /// <summary>La tienda verificó el pago: cuenta como pagado y, si cubre el saldo, liquida y descuenta el stock.</summary>
    Task<ApartadoDto> ConfirmarPagoAsync(string apartadoId, string abonoId, string usuarioId, CancellationToken ct = default);

    /// <summary>La tienda no encontró el pago. Queda el motivo y el pedido sigue activo hasta que venza.</summary>
    Task<ApartadoDto> RechazarPagoAsync(string apartadoId, string abonoId, string motivo, string usuarioId, CancellationToken ct = default);

    /// <summary>Corte del día de una sucursal: ventas liquidadas y cerradas ese día (hora de México).</summary>
    Task<CorteDiaDto> CorteDiaAsync(string sucursal, CancellationToken ct = default);

    /// <summary>Métricas del dashboard de la dueña, opcionalmente filtradas por sucursal.</summary>
    Task<MetricasDto> MetricasAsync(string? sucursal, CancellationToken ct = default);
}

/// <summary>Comprobantes de transferencia: la clienta los sube; el personal los ve con su PIN.</summary>
public interface IComprobanteService
{
    Task<ComprobanteFirmadoDto> FirmarSubidaAsync(string apartadoId, string contentType, string clientaId, CancellationToken ct = default);
    Task<ApartadoDto> RegistrarAsync(string apartadoId, string clave, string clientaId, CancellationToken ct = default);
    Task<ComprobanteVistaDto> VerAsync(string apartadoId, string abonoId, string pin, string usuarioId, CancellationToken ct = default);
}

/// <summary>Configuración de negocio de la boutique.</summary>
public interface IConfigService
{
    Task<ConfigBoutiqueDto> ObtenerAsync(CancellationToken ct = default);
    Task<ConfigBoutiqueDto> FijarSuspendidaAsync(bool suspendida, CancellationToken ct = default);
    Task<ConfigBoutiqueDto> FijarCatalogoPublicoAsync(bool publico, CancellationToken ct = default);

    /// <summary>Cuenta para recibir transferencias. Solo la ve quien tiene sesión (no es parte de la config pública).</summary>
    Task<DatosBancariosDto> ObtenerDatosBancariosAsync(CancellationToken ct = default);
    Task<DatosBancariosDto> FijarDatosBancariosAsync(DatosBancariosInput input, CancellationToken ct = default);
}

/// <summary>Avisos in-app de la clienta: derivados del estado de sus apartados.</summary>
public interface IAvisoService
{
    Task<List<AvisoDto>> ListarAsync(string clientaId, CancellationToken ct = default);
    Task MarcarLeidosAsync(string clientaId, List<string> avisoIds, CancellationToken ct = default);
}

/// <summary>Casos de uso del CRUD de prenda.</summary>
public interface IProductoService
{
    Task<List<ProductoDto>> ListarAsync(bool incluirInactivos, CancellationToken ct = default);
    Task<ProductoDto> ObtenerAsync(string id, CancellationToken ct = default);
    Task<ProductoDto> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default);
    Task<ProductoDto> CrearAsync(NuevoProductoInput input, CancellationToken ct = default);
    Task<ProductoDto> EditarAsync(string id, EditarProductoInput input, CancellationToken ct = default);
    Task<ProductoDto> AjustarStockAsync(string id, AjusteStockInput input, CancellationToken ct = default);
    Task<ProductoDto> AgregarColorAsync(string id, AgregarColorInput input, CancellationToken ct = default);
    Task<ProductoDto> QuitarImagenAsync(string id, string url, CancellationToken ct = default);
    Task<ProductoDto> AgregarImagenesColorAsync(string id, string colorId, List<string> urls, CancellationToken ct = default);
    Task<ProductoDto> QuitarColorAsync(string id, string colorId, CancellationToken ct = default);
    Task DesactivarAsync(string id, CancellationToken ct = default);
    Task ActivarAsync(string id, CancellationToken ct = default);

    /// <summary>Borrado físico permanente: solo si está inactiva y sin stock físico.</summary>
    Task EliminarPermanenteAsync(string id, CancellationToken ct = default);
}

/// <summary>Movimientos de inventario con historial (guardar en lote, mover entre sucursales).</summary>
public interface IStockService
{
    Task<GuardarStockResultado> GuardarLoteAsync(string productoId, GuardarStockInput input, CancellationToken ct = default);
    Task MoverAsync(MoverStockInput input, CancellationToken ct = default);
}

/// <summary>CRUD de categorías del catálogo (con sus tallas).</summary>
public interface ICategoriaService
{
    Task<List<CategoriaDto>> ListarAsync(CancellationToken ct = default);
    Task<CategoriaDto> CrearAsync(CategoriaInput input, CancellationToken ct = default);
    Task<CategoriaDto> EditarAsync(string id, CategoriaInput input, CancellationToken ct = default);
    Task EliminarAsync(string id, CancellationToken ct = default);
}

/// <summary>CRUD del catálogo de colores.</summary>
public interface IColorService
{
    Task<List<ColorCatalogoDto>> ListarAsync(CancellationToken ct = default);
    Task<ColorCatalogoDto> CrearAsync(ColorInput input, CancellationToken ct = default);
    Task<ColorCatalogoDto> EditarAsync(string id, ColorInput input, CancellationToken ct = default);
    Task EliminarAsync(string id, CancellationToken ct = default);
}



/// <summary>
/// Lógica de idempotencia reutilizable por el filtro de la Api. Expone el inicio (consulta/reserva)
/// y el cierre (guardar la respuesta 2xx) sin acoplarse a HTTP.
/// </summary>
public interface IIdempotenciaService
{
    Task<InicioIdempotencia> IniciarAsync(string usuarioId, string ruta, string? llave, CancellationToken ct = default);
    Task CompletarAsync(string usuarioId, string ruta, string llave, int codigoEstado, string? cuerpo, CancellationToken ct = default);
}
