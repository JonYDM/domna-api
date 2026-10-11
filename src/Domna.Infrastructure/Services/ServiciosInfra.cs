using Domna.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Domna.Infrastructure.Services;

/// <summary>Hash del PIN con BCrypt (work factor por defecto 11).</summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string valor) => BCrypt.Net.BCrypt.HashPassword(valor, workFactor: 11);

    public bool Verificar(string valor, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(valor, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

/// <summary>Reloj del sistema en UTC.</summary>
public class RelojSistema : IReloj
{
    public DateTime Ahora => DateTime.UtcNow;
}

/// <summary>
/// Correo simulado para la demo: no envía nada real, solo registra en el log que "se envió" el
/// enlace de recuperación. En producción se reemplaza por un proveedor transaccional (SendGrid, etc.).
/// </summary>
public class CorreoSimulado : IServicioCorreo
{
    private readonly ILogger<CorreoSimulado> _log;

    public CorreoSimulado(ILogger<CorreoSimulado> log) => _log = log;

    public Task EnviarRecuperacionAsync(string email, string token, CancellationToken ct = default)
    {
        _log.LogInformation("[correo simulado] Recuperación de PIN para {Email}. Token: {Token}", email, token);
        return Task.CompletedTask;
    }
}
