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
