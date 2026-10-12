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
