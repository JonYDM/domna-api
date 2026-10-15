using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Domna.Application.Abstractions;
using Domna.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Domna.Infrastructure.Services;

/// <summary>Opciones del JWT (se enlazan desde appsettings "Jwt").</summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "domna-api";
    public string Audience { get; set; } = "domna-web";
    /// <summary>Clave secreta HMAC. En producción va en variables de entorno / secretos.</summary>
    public string Secret { get; set; } = "domna-dev-secret-cambia-esto-por-uno-de-32-bytes-o-mas";
    public int MinutosVigencia { get; set; } = 60 * 24 * 7; // 7 días
}

/// <summary>Genera JWT HS256 con el id, rol y datos básicos de la clienta.</summary>
public class JwtGenerator : IJwtGenerator
{
    private readonly JwtOptions _opt;

    public JwtGenerator(JwtOptions opt) => _opt = opt;

    public (string Token, DateTime Expira) Generar(Clienta clienta, Rol rol, string sesionId)
    {
        var expira = DateTime.UtcNow.AddMinutes(_opt.MinutosVigencia);
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Secret));
        var creds = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clienta.Id),
            new Claim(ClaimTypes.Role, rol.ToString().ToLowerInvariant()),
            new Claim("telefono", clienta.Telefono),
            new Claim("nombre", clienta.Nombre),
            // Id de la sesión: se valida en cada petición para forzar "una sola sesión activa".
            new Claim("sid", sesionId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: expira,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
