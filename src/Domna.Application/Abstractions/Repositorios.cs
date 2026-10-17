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

    /// <summary>Solo rol Clienta, para el panel de Clientas de la dueña (no incluye staff).</summary>
    Task<List<Clienta>> ListarClientasAsync(CancellationToken ct = default);

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

    /// <summary>Prendas por id (activas o no) con sus colores y fotos, sin variantes: para mostrar la foto de un pedido.</summary>
    Task<List<Producto>> ListarFotosAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default);
    Task<Producto?> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default);

    /// <summary>El producto que contiene una variante (con colores, variantes e imágenes).</summary>
    Task<Producto?> ObtenerPorVarianteAsync(string varianteId, CancellationToken ct = default);
    Task AgregarAsync(Producto producto, CancellationToken ct = default);
    Task EliminarAsync(Producto producto, CancellationToken ct = default);
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

/// <summary>Persistencia de apartados/ventas y el cálculo de piezas reservadas (stock on-the-fly).</summary>
public interface IApartadoRepository
{
    Task<Apartado?> ObtenerPorIdAsync(string id, CancellationToken ct = default);
    Task<List<Apartado>> ListarAsync(CancellationToken ct = default);
    Task<List<Apartado>> ListarDeClientaAsync(string clientaId, CancellationToken ct = default);

    /// <summary>Apartados ACTIVOS de una clienta (para el límite de apartados simultáneos).</summary>
    Task<int> ContarActivosDeClientaAsync(string clientaId, CancellationToken ct = default);

    /// <summary>Apartados activos cuya vigencia ya pasó (para procesarlos al vencimiento).</summary>
    Task<List<Apartado>> ListarActivosVencidosAsync(DateTime hoy, CancellationToken ct = default);

    /// <summary>
    /// Piezas reservadas de una variante en apartados ACTIVOS, por sucursal de origen.
    /// disponible = stock físico − reservado (se calcula aquí, no se persiste).
    /// </summary>
    Task<Dictionary<string, int>> ObtenerReservadoAsync(string varianteId, CancellationToken ct = default);

    /// <summary>Igual que arriba, pero para varias variantes a la vez (listados de catálogo).</summary>
    Task<Dictionary<string, Dictionary<string, int>>> ObtenerReservadoPorVariantesAsync(
        IEnumerable<string> varianteIds, CancellationToken ct = default);

    /// <summary>
    /// Mayor número de folio existente (el "N" de "DOM-NNNN"). Base para el siguiente folio sin
    /// reutilizar números, aunque haya huecos. 0 si todavía no hay apartados.
    /// </summary>
    Task<int> ObtenerMaxFolioAsync(CancellationToken ct = default);

    /// <summary>
    /// ¿La excepción al guardar es una violación del índice único de Folio? (dos pedidos simultáneos
    /// tomaron el mismo número). Permite reintentar el alta con otro folio sin acoplar el caso de uso
    /// al proveedor (Npgsql / InMemory).
    /// </summary>
    bool EsColisionDeFolio(Exception ex);

    Task AgregarAsync(Apartado apartado, CancellationToken ct = default);
    Task GuardarAsync(CancellationToken ct = default);
}
public interface IConfigRepository
{
    Task<ConfigBoutique> ObtenerAsync(CancellationToken ct = default);
    Task GuardarAsync(CancellationToken ct = default);
}

/// <summary>Qué avisos ya leyó cada clienta. Los avisos mismos se derivan, no se persisten.</summary>
public interface IAvisoRepository
{
    Task<HashSet<string>> ObtenerLeidosAsync(string clientaId, CancellationToken ct = default);
    Task MarcarLeidosAsync(string clientaId, IEnumerable<string> avisoIds, CancellationToken ct = default);
}


/// <summary>Historial de movimientos de inventario. AgregarAsync no guarda: se persiste junto con el producto.</summary>
public interface IMovimientoRepository
{
    Task AgregarAsync(MovimientoInventario movimiento, CancellationToken ct = default);
    Task<List<MovimientoInventario>> ListarDeProductoAsync(string productoId, CancellationToken ct = default);
}


/// <summary>
/// Persistencia de registros de idempotencia (una tabla propia, apta para varias instancias).
/// La llave se acota por usuario+ruta mediante el índice único (UsuarioId, Ruta, Llave).
/// </summary>
public interface IIdempotenciaRepository
{
    Task<Idempotencia?> ObtenerAsync(string usuarioId, string ruta, string llave, CancellationToken ct = default);

    /// <summary>
    /// Inserta y persiste el registro "en curso". Devuelve true si lo creó; false si otra petición
    /// con la misma llave ya lo había insertado (choque del índice único: carrera entre instancias).
    /// </summary>
    Task<bool> IntentarReservarAsync(Idempotencia registro, CancellationToken ct = default);

    /// <summary>Guarda los cambios (p. ej. al completar un registro ya reservado).</summary>
    Task GuardarAsync(CancellationToken ct = default);

    /// <summary>Borra el registro "en curso" cuando la operación no fue 2xx (para poder reintentarla).</summary>
    Task EliminarAsync(Idempotencia registro, CancellationToken ct = default);

    /// <summary>Limpieza perezosa: borra registros con más de 24 h (sin job aparte). Devuelve cuántos borró.</summary>
    Task<int> LimpiarVencidosAsync(DateTime limite, CancellationToken ct = default);
}


/// <summary>
/// Persistencia de las sesiones activas (una tabla propia). Soporta la regla de "una sola sesión
/// activa por cuenta": listar/crear sesiones y revocarlas (una o todas las de un usuario).
/// </summary>
public interface ISesionRepository
{
    Task<Sesion?> ObtenerPorIdAsync(string id, CancellationToken ct = default);

    /// <summary>Sesiones NO revocadas de un usuario (puede incluir vencidas; el servicio filtra por fecha).</summary>
    Task<List<Sesion>> ListarNoRevocadasDeUsuarioAsync(string usuarioId, CancellationToken ct = default);

    Task AgregarAsync(Sesion sesion, CancellationToken ct = default);

    /// <summary>Confirma los cambios pendientes (altas y revocaciones).</summary>
    Task GuardarAsync(CancellationToken ct = default);

    /// <summary>Limpieza perezosa opcional: borra sesiones vencidas desde hace tiempo. Devuelve cuántas borró.</summary>
    Task<int> LimpiarVencidasAsync(DateTime limite, CancellationToken ct = default);
}
