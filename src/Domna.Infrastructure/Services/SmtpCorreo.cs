using System.Net;
using System.Net.Mail;
using Domna.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Domna.Infrastructure.Services;

/// <summary>
/// Opciones del correo saliente (sección "Correo" de la configuración / variables de entorno).
/// Para Gmail: Host "smtp.gmail.com", Port 587, Usuario = tu correo, Password = contraseña de
/// aplicación de 16 caracteres (NO la contraseña normal). Las credenciales nunca van en el código.
/// </summary>
public class CorreoOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Usuario { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Remitente mostrado. Si queda vacío, se usa el Usuario.</summary>
    public string Remitente { get; set; } = "";
    public string NombreRemitente { get; set; } = "Domna";
    public bool UsarSsl { get; set; } = true;
    /// <summary>Base del enlace de recuperación que se arma en el correo (p. ej. la URL del front).</summary>
    public string BaseEnlaceRecuperacion { get; set; } = "http://localhost:5173/entrar/recuperar";

    /// <summary>¿Hay credenciales mínimas para enviar correo real?</summary>
    public bool Configurado =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Usuario)
        && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Envía el enlace de recuperación de PIN por SMTP (Gmail u otro proveedor).</summary>
public class SmtpCorreo : IServicioCorreo
{
    private readonly CorreoOptions _opt;
    private readonly ILogger<SmtpCorreo> _log;

    public SmtpCorreo(CorreoOptions opt, ILogger<SmtpCorreo> log)
    {
        _opt = opt;
        _log = log;
    }

    public async Task EnviarRecuperacionAsync(string email, string token, CancellationToken ct = default)
    {
        var remitente = string.IsNullOrWhiteSpace(_opt.Remitente) ? _opt.Usuario : _opt.Remitente;
        var separador = _opt.BaseEnlaceRecuperacion.Contains('?') ? "&" : "?";
        var enlace = $"{_opt.BaseEnlaceRecuperacion}{separador}token={Uri.EscapeDataString(token)}";

        using var mensaje = new MailMessage
        {
            From = new MailAddress(remitente, _opt.NombreRemitente),
            Subject = "Recupera tu PIN de Domna",
            IsBodyHtml = true,
            Body = $"""
                <div style="font-family:system-ui,Arial,sans-serif;max-width:480px;margin:auto;color:#1F1B24">
                  <h2 style="color:#1F1B24">Recupera tu PIN</h2>
                  <p>Recibimos una solicitud para restablecer tu PIN de acceso a <strong>Domna</strong>.</p>
                  <p>
                    <a href="{enlace}"
                       style="display:inline-block;background:#C0283E;color:#fff;text-decoration:none;
                              padding:12px 20px;border-radius:8px;font-weight:600">
                      Crear un PIN nuevo
                    </a>
                  </p>
                  <p style="color:#8C8790;font-size:13px">
                    O usa este código: <strong>{token}</strong><br>
                    El enlace vence en 30 minutos y solo puede usarse una vez.
                  </p>
                  <p style="color:#8C8790;font-size:13px">Si no fuiste tú, ignora este correo.</p>
                </div>
                """
        };
        mensaje.To.Add(email);

        using var cliente = new SmtpClient(_opt.Host, _opt.Port)
        {
            EnableSsl = _opt.UsarSsl,
            Credentials = new NetworkCredential(_opt.Usuario, _opt.Password)
        };

        await cliente.SendMailAsync(mensaje, ct);
        _log.LogInformation("Correo de recuperación enviado a {Email}", email);
    }
}
