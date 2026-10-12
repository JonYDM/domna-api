namespace Domna.Application.Dtos;

/// <summary>Datos públicos de la clienta (sin PIN ni hash). Equivale a la interfaz Clienta del front.</summary>
public record ClientaDto(
    string Id,
    string Nombre,
    string Apellidos,
    string Telefono,
    string Email,
    string Origen,
    string Rol,
    string? SucursalCajera,
    DateTime Creada,
    DateTime? UltimoAcceso,
    decimal PenalizacionPendiente,
    bool Activo);

/// <summary>Alta propia de la clienta (sign-in): nombre, apellidos, teléfono +52, correo y PIN de 6 dígitos.</summary>
public record RegistroClientaInput(
    string Nombre,
    string Apellidos,
    string Telefono,
    string Email,
    string Pin);

/// <summary>Login con teléfono + PIN.</summary>
public record LoginInput(string Telefono, string Pin);

/// <summary>Respuesta de registro/login: el JWT, cuándo expira y la clienta.</summary>
public record SesionDto(string Token, DateTime Expira, ClientaDto Clienta);

/// <summary>"Olvidé mi PIN": la clienta escribe su correo registrado.</summary>
public record SolicitarRecuperacionInput(string Email);

/// <summary>
/// Resultado de solicitar la recuperación. En la demo se devuelve el token (el front lo muestra);
/// en producción el token viaja por correo y no se revela si el correo existe.
/// </summary>
public record RecuperacionDto(string? Token);

/// <summary>Confirmar la recuperación: token + PIN nuevo.</summary>
public record ConfirmarRecuperacionInput(string Token, string Pin);
