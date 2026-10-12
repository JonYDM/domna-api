using Domna.Domain;

namespace Domna.Application.Abstractions;

/// <summary>Persistencia de clientas y tokens de recuperación.</summary>
public interface IClientaRepository
{
    Task<Clienta?> ObtenerPorIdAsync(string id, CancellationToken ct = default);
    Task<Clienta?> ObtenerPorTelefonoAsync(string telefono, CancellationToken ct = default);
    Task<Clienta?> ObtenerPorEmailAsync(string email, CancellationToken ct = default);
    Task AgregarAsync(Clienta clienta, CancellationToken ct = default);

    /// <summary>Dueña(s) y cajeras, ordenadas por rol y nombre (para el panel de Personal).</summary>
    Task<List<Clienta>> ListarStaffAsync(CancellationToken ct = default);

    Task AgregarRecuperacionAsync(RecuperacionPin recuperacion, CancellationToken ct = default);
    Task<RecuperacionPin?> ObtenerRecuperacionAsync(string token, CancellationToken ct = default);

    /// <summary>Confirma los cambios pendientes en la unidad de trabajo.</summary>
    Task GuardarAsync(CancellationToken ct = default);
}

/// <summary>Persistencia de productos (prendas) con sus colores y variantes.</summary>
public interface IProductoRepository
{
    Task<Producto?> ObtenerPorIdAsync(string id, CancellationToken ct = default);
    Task<List<Producto>> ListarAsync(bool incluirInactivos, CancellationToken ct = default);
    Task<Producto?> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default);
    Task AgregarAsync(Producto producto, CancellationToken ct = default);
    Task GuardarAsync(CancellationToken ct = default);

    /// <summary>Cuántas prendas usan una categoría (para no borrar categorías en uso).</summary>
    Task<int> ContarPorCategoriaAsync(string categoriaId, CancellationToken ct = default);
}

/// <summary>Persistencia de categorías del catálogo.</summary>
public interface ICategoriaRepository
{
    Task<Categoria?> ObtenerPorIdAsync(string id, CancellationToken ct = default);
    Task<List<Categoria>> ListarAsync(CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, string? exceptoId = null, CancellationToken ct = default);
    Task AgregarAsync(Categoria categoria, CancellationToken ct = default);
    Task EliminarAsync(Categoria categoria, CancellationToken ct = default);
    Task GuardarAsync(CancellationToken ct = default);
}

/// <summary>Persistencia del catálogo de colores.</summary>
public interface IColorRepository
{
    Task<ColorCatalogo?> ObtenerPorIdAsync(string id, CancellationToken ct = default);
    Task<List<ColorCatalogo>> ListarAsync(CancellationToken ct = default);
    Task<bool> ExisteNombreAsync(string nombre, string? exceptoId = null, CancellationToken ct = default);
    Task AgregarAsync(ColorCatalogo color, CancellationToken ct = default);
    Task EliminarAsync(ColorCatalogo color, CancellationToken ct = default);
    Task GuardarAsync(CancellationToken ct = default);
}
