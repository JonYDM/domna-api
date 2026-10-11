using Domna.Domain;

namespace Domna.Application.Abstractions;

/// <summary>Hashing del PIN. La implementación usa BCrypt.</summary>
public interface IPasswordHasher
{
    string Hash(string valor);
    bool Verificar(string valor, string hash);
}

/// <summary>Generación de tokens JWT para la sesión de la clienta.</summary>
public interface IJwtGenerator
{
    /// <summary>Genera un JWT con el id/rol de la clienta. Devuelve el token y su expiración.</summary>
    (string Token, DateTime Expira) Generar(Clienta clienta, Rol rol);
}

/// <summary>Envío del enlace/código de recuperación. En la demo solo lo registra (no manda correo real).</summary>
public interface IServicioCorreo
{
    Task EnviarRecuperacionAsync(string email, string token, CancellationToken ct = default);
}

/// <summary>Reloj inyectable (facilita las pruebas y el reloj de demo).</summary>
public interface IReloj
{
    DateTime Ahora { get; }
}
