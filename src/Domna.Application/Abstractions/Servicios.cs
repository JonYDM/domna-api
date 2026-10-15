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
    /// <summary>
    /// Genera un JWT con el id/rol de la clienta y el id de la sesión (claim `sid`).
    /// Devuelve el token y su expiración. El `sesionId` se genera antes de llamar aquí para poder
    /// crear la Sesión con la misma expiración del token.
    /// </summary>
    (string Token, DateTime Expira) Generar(Clienta clienta, Rol rol, string sesionId);
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

/// <summary>
/// Caché de la validación de sesiones por `sid` (para no pegarle a la base en cada petición). La
/// implementación real usa IMemoryCache; en pruebas puede ser un no-op. Al revocar una sesión en la
/// misma instancia se invalida su entrada para que el 401 sea inmediato.
/// </summary>
public interface ICacheSesion
{
    /// <summary>Olvida el estado cacheado de un `sid` (tras revocarlo en esta instancia).</summary>
    void Invalidar(string sesionId);
}
