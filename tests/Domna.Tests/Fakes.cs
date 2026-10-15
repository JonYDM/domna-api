using Domna.Application.Abstractions;
using Domna.Domain;

namespace Domna.Tests;

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public class RelojFijo : IReloj
{
    public DateTime Ahora { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}

/// <summary>Hasher trivial para pruebas (prefijo + valor). No usa BCrypt para ir rápido.</summary>
public class HasherFake : IPasswordHasher
{
    public string Hash(string valor) => "h:" + valor;
    public bool Verificar(string valor, string hash) => hash == "h:" + valor;
}

/// <summary>JWT falso: token predecible.</summary>
public class JwtFake : IJwtGenerator
{
    public (string Token, DateTime Expira) Generar(Clienta clienta, Rol rol)
        => ($"token-{clienta.Id}-{rol}", DateTime.UtcNow.AddDays(7));
}

/// <summary>Correo que recuerda el último token "enviado".</summary>
public class CorreoFake : IServicioCorreo
{
    public string? UltimoToken { get; private set; }
    public string? UltimoEmail { get; private set; }

    public Task EnviarRecuperacionAsync(string email, string token, CancellationToken ct = default)
    {
        UltimoEmail = email;
        UltimoToken = token;
        return Task.CompletedTask;
    }
}

/// <summary>Repositorio de clientas en memoria.</summary>
public class ClientaRepoFake : IClientaRepository
{
    private readonly List<Clienta> _clientas = new();
    private readonly List<RecuperacionPin> _recuperaciones = new();

    public ClientaRepoFake(params Clienta[] iniciales) => _clientas.AddRange(iniciales);

    public Task<Clienta?> ObtenerPorIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_clientas.FirstOrDefault(c => c.Id == id));

    public Task<Clienta?> ObtenerPorTelefonoAsync(string telefono, CancellationToken ct = default)
        => Task.FromResult(_clientas.FirstOrDefault(c => c.Telefono == telefono));

    public Task<Clienta?> ObtenerPorEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(_clientas.FirstOrDefault(c => c.Email == email));

    public Task<List<Clienta>> ListarStaffAsync(CancellationToken ct = default)
        => Task.FromResult(_clientas.Where(c => c.Rol != Rol.Clienta).OrderBy(c => c.Rol).ThenBy(c => c.Nombre).ToList());

    public Task<List<Clienta>> ListarClientasAsync(CancellationToken ct = default)
        => Task.FromResult(_clientas.Where(c => c.Rol == Rol.Clienta).OrderByDescending(c => c.Creada).ToList());

    public Task AgregarAsync(Clienta clienta, CancellationToken ct = default)
    {
        _clientas.Add(clienta);
        return Task.CompletedTask;
    }

    public Task AgregarRecuperacionAsync(RecuperacionPin recuperacion, CancellationToken ct = default)
    {
        _recuperaciones.Add(recuperacion);
        return Task.CompletedTask;
    }

    public Task<RecuperacionPin?> ObtenerRecuperacionAsync(string token, CancellationToken ct = default)
        => Task.FromResult(_recuperaciones.FirstOrDefault(r => r.Token == token));

    public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Repositorio de productos en memoria.</summary>
public class ProductoRepoFake : IProductoRepository
{
    private readonly List<Producto> _productos = new();

    public Task<Producto?> ObtenerPorIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_productos.FirstOrDefault(p => p.Id == id));

    public Task<List<Producto>> ListarAsync(bool incluirInactivos, CancellationToken ct = default)
        => Task.FromResult(_productos.Where(p => incluirInactivos || p.Activo)
            .OrderByDescending(p => p.Creado).ToList());

    public Task<Producto?> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default)
        => Task.FromResult(_productos.FirstOrDefault(p => p.BuscarPorCodigoBarras(codigo) is not null));

    public Task AgregarAsync(Producto producto, CancellationToken ct = default)
    {
        _productos.Add(producto);
        return Task.CompletedTask;
    }

    public Task<Producto?> ObtenerPorVarianteAsync(string varianteId, CancellationToken ct = default)
        => Task.FromResult(_productos.FirstOrDefault(p => p.Variantes.Any(v => v.Id == varianteId)));

    public Task EliminarAsync(Producto producto, CancellationToken ct = default)
    {
        _productos.RemoveAll(p => p.Id == producto.Id);
        return Task.CompletedTask;
    }

    public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> ContarPorCategoriaAsync(string categoriaId, CancellationToken ct = default)
        => Task.FromResult(_productos.Count(p => p.CategoriaId == categoriaId));
}

/// <summary>Repositorio de categorías en memoria.</summary>
public class CategoriaRepoFake : ICategoriaRepository
{
    private readonly List<Categoria> _categorias = new();

    public CategoriaRepoFake(params Categoria[] iniciales) => _categorias.AddRange(iniciales);

    public Task<Categoria?> ObtenerPorIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_categorias.FirstOrDefault(c => c.Id == id));

    public Task<List<Categoria>> ListarAsync(CancellationToken ct = default)
        => Task.FromResult(_categorias.OrderBy(c => c.Nombre).ToList());

    public Task<bool> ExisteNombreAsync(string nombre, string? exceptoId = null, CancellationToken ct = default)
        => Task.FromResult(_categorias.Any(c => c.Id != exceptoId && Atributos.MismoNombre(c.Nombre, nombre)));

    public Task AgregarAsync(Categoria categoria, CancellationToken ct = default)
    {
        _categorias.Add(categoria);
        return Task.CompletedTask;
    }

    public Task EliminarAsync(Categoria categoria, CancellationToken ct = default)
    {
        _categorias.Remove(categoria);
        return Task.CompletedTask;
    }

    public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Repositorio de colores en memoria.</summary>
public class ColorRepoFake : IColorRepository
{
    private readonly List<ColorCatalogo> _colores = new();

    public ColorRepoFake(params ColorCatalogo[] iniciales) => _colores.AddRange(iniciales);

    public Task<ColorCatalogo?> ObtenerPorIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_colores.FirstOrDefault(c => c.Id == id));

    public Task<List<ColorCatalogo>> ListarAsync(CancellationToken ct = default)
        => Task.FromResult(_colores.OrderBy(c => c.Nombre).ToList());

    public Task<bool> ExisteNombreAsync(string nombre, string? exceptoId = null, CancellationToken ct = default)
        => Task.FromResult(_colores.Any(c => c.Id != exceptoId && Atributos.MismoNombre(c.Nombre, nombre)));

    public Task AgregarAsync(ColorCatalogo color, CancellationToken ct = default)
    {
        _colores.Add(color);
        return Task.CompletedTask;
    }

    public Task EliminarAsync(ColorCatalogo color, CancellationToken ct = default)
    {
        _colores.Remove(color);
        return Task.CompletedTask;
    }

    public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Repositorio de apartados en memoria, con el cálculo on-the-fly de reservado (igual al real).</summary>
public class ApartadoRepoFake : IApartadoRepository
{
    private readonly List<Apartado> _apartados = new();

    public ApartadoRepoFake(params Apartado[] iniciales) => _apartados.AddRange(iniciales);

    public Task<Apartado?> ObtenerPorIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_apartados.FirstOrDefault(a => a.Id == id));

    public Task<List<Apartado>> ListarAsync(CancellationToken ct = default)
        => Task.FromResult(_apartados.OrderByDescending(a => a.Creado).ToList());

    public Task<List<Apartado>> ListarDeClientaAsync(string clientaId, CancellationToken ct = default)
        => Task.FromResult(_apartados.Where(a => a.ClientaId == clientaId).OrderByDescending(a => a.Creado).ToList());

    public Task<int> ContarActivosDeClientaAsync(string clientaId, CancellationToken ct = default)
        => Task.FromResult(_apartados.Count(a => a.ClientaId == clientaId && a.Estado == EstadoApartado.Activo
            && a.Modalidad != ModalidadApartado.Compra));

    public Task<List<Apartado>> ListarActivosVencidosAsync(DateTime hoy, CancellationToken ct = default)
        => Task.FromResult(_apartados.Where(a => a.Estado == EstadoApartado.Activo && a.VenceEl <= hoy).ToList());

    public Task<Dictionary<string, int>> ObtenerReservadoAsync(string varianteId, CancellationToken ct = default)
    {
        var filas = _apartados
            .Where(a => a.Estado == EstadoApartado.Activo)
            .SelectMany(a => a.Lineas)
            .Where(l => l.VarianteId == varianteId)
            .GroupBy(l => l.SucursalOrigen)
            .ToDictionary(g => g.Key == SucursalId.Temixco ? "temixco" : "azteca", g => g.Sum(l => l.Cantidad));
        return Task.FromResult(filas);
    }

    public Task<Dictionary<string, Dictionary<string, int>>> ObtenerReservadoPorVariantesAsync(
        IEnumerable<string> varianteIds, CancellationToken ct = default)
    {
        var ids = varianteIds.ToHashSet();
        var resultado = _apartados
            .Where(a => a.Estado == EstadoApartado.Activo)
            .SelectMany(a => a.Lineas)
            .Where(l => ids.Contains(l.VarianteId))
            .GroupBy(l => l.VarianteId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(l => l.SucursalOrigen)
                    .ToDictionary(gg => gg.Key == SucursalId.Temixco ? "temixco" : "azteca", gg => gg.Sum(l => l.Cantidad)));
        return Task.FromResult(resultado);
    }

    public Task AgregarAsync(Apartado apartado, CancellationToken ct = default)
    {
        _apartados.Add(apartado);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cuántas veces el próximo GuardarAsync debe simular una colisión del índice único de Folio
    /// (dos pedidos simultáneos). Cada guardado fallido lo decrementa. Sirve para probar el reintento.
    /// </summary>
    public int ColisionesDeFolioPendientes { get; set; }

    /// <summary>Folios con los que se intentó guardar, en orden (para verificar el reintento).</summary>
    public List<string> FoliosIntentados { get; } = new();

    public Task GuardarAsync(CancellationToken ct = default)
    {
        // Registra el folio del último apartado agregado (el que se intenta persistir).
        var ultimo = _apartados.LastOrDefault();
        if (ultimo is not null) FoliosIntentados.Add(ultimo.Folio);

        if (ColisionesDeFolioPendientes > 0)
        {
            ColisionesDeFolioPendientes--;
            throw new FolioDuplicadoFake();
        }
        return Task.CompletedTask;
    }

    public Task<int> ObtenerMaxFolioAsync(CancellationToken ct = default)
    {
        var max = 0;
        foreach (var a in _apartados)
        {
            var guion = a.Folio.LastIndexOf('-');
            var parte = guion >= 0 ? a.Folio[(guion + 1)..] : a.Folio;
            if (int.TryParse(parte, out var n) && n > max) max = n;
        }
        return Task.FromResult(max);
    }

    public bool EsColisionDeFolio(Exception ex) => ex is FolioDuplicadoFake;
}

/// <summary>Excepción que imita la violación del índice único de Folio en el repositorio fake.</summary>
public class FolioDuplicadoFake : Exception
{
    public FolioDuplicadoFake() : base("Folio duplicado (simulado).") { }
}

/// <summary>Config en memoria, ya creada con defaults (no simula el "crear si no existe").</summary>
public class ConfigRepoFake : IConfigRepository
{
    public ConfigBoutique Config { get; }

    public ConfigRepoFake(ConfigBoutique? config = null) =>
        Config = config ?? ConfigBoutique.CrearDefault("Jesly Boutique", "jesly-boutique");

    public Task<ConfigBoutique> ObtenerAsync(CancellationToken ct = default) => Task.FromResult(Config);
    public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Avisos leídos en memoria.</summary>
public class AvisoRepoFake : IAvisoRepository
{
    private readonly Dictionary<string, HashSet<string>> _leidos = new();

    public Task<HashSet<string>> ObtenerLeidosAsync(string clientaId, CancellationToken ct = default)
        => Task.FromResult(_leidos.TryGetValue(clientaId, out var s) ? new HashSet<string>(s) : new HashSet<string>());

    public Task MarcarLeidosAsync(string clientaId, IEnumerable<string> avisoIds, CancellationToken ct = default)
    {
        if (!_leidos.TryGetValue(clientaId, out var s))
        {
            s = new HashSet<string>();
            _leidos[clientaId] = s;
        }
        foreach (var id in avisoIds) s.Add(id);
        return Task.CompletedTask;
    }
}

/// <summary>Historial de movimientos en memoria.</summary>
public class MovimientoRepoFake : IMovimientoRepository
{
    public List<MovimientoInventario> Todos { get; } = new();

    public Task AgregarAsync(MovimientoInventario movimiento, CancellationToken ct = default)
    {
        Todos.Add(movimiento);
        return Task.CompletedTask;
    }

    public Task<List<MovimientoInventario>> ListarDeProductoAsync(string productoId, CancellationToken ct = default)
        => Task.FromResult(Todos.Where(m => m.ProductoId == productoId).OrderByDescending(m => m.Fecha).ToList());
}


/// <summary>
/// Repositorio de idempotencia en memoria. Imita el índice único (UsuarioId, Ruta, Llave):
/// IntentarReservarAsync devuelve false si ya existe un registro con esa llave acotada.
/// </summary>
public class IdempotenciaRepoFake : IIdempotenciaRepository
{
    private readonly List<Idempotencia> _registros = new();

    /// <summary>Cuántos registros borró la última limpieza perezosa (para verificarla en pruebas).</summary>
    public int UltimosLimpiados { get; private set; }

    private static bool Coincide(Idempotencia x, string usuarioId, string ruta, string llave) =>
        x.UsuarioId == usuarioId && x.Ruta == ruta && x.Llave == llave;

    public Task<Idempotencia?> ObtenerAsync(string usuarioId, string ruta, string llave, CancellationToken ct = default)
        => Task.FromResult(_registros.FirstOrDefault(x => Coincide(x, usuarioId, ruta, llave)));

    public Task<bool> IntentarReservarAsync(Idempotencia registro, CancellationToken ct = default)
    {
        if (_registros.Any(x => Coincide(x, registro.UsuarioId, registro.Ruta, registro.Llave)))
            return Task.FromResult(false);
        _registros.Add(registro);
        return Task.FromResult(true);
    }

    public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task EliminarAsync(Idempotencia registro, CancellationToken ct = default)
    {
        _registros.Remove(registro);
        return Task.CompletedTask;
    }

    public Task<int> LimpiarVencidosAsync(DateTime limite, CancellationToken ct = default)
    {
        UltimosLimpiados = _registros.RemoveAll(x => x.CreadoEl < limite);
        return Task.FromResult(UltimosLimpiados);
    }
}
