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
}

/// <summary>Configuración de negocio de la boutique.</summary>
public interface IConfigService
{
    Task<ConfigBoutiqueDto> ObtenerAsync(CancellationToken ct = default);
    Task<ConfigBoutiqueDto> FijarSuspendidaAsync(bool suspendida, CancellationToken ct = default);
    Task<ConfigBoutiqueDto> FijarCatalogoPublicoAsync(bool publico, CancellationToken ct = default);
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
