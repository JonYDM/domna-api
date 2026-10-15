namespace Domna.Domain;

/// <summary>
/// Error de negocio con un código HTTP asociado. Equivale al ApiError del front
/// (src/lib/errores.ts): 400 validación, 401 credenciales, 404 no encontrado, 409 conflicto.
/// El middleware de la Api lo traduce a la respuesta HTTP correspondiente.
/// </summary>
public sealed class DomnaException : Exception
{
    public int StatusCode { get; }

    /// <summary>
    /// Código corto y estable que el front usa para distinguir el caso (p. ej. "sesion_activa",
    /// "sesion_reemplazada"). Opcional: si es null, el cuerpo de error queda igual que siempre.
    /// </summary>
    public string? Codigo { get; }

    /// <summary>Datos extra para el front (p. ej. el dispositivo de la otra sesión). Opcional.</summary>
    public object? Datos { get; }

    public DomnaException(int statusCode, string message, string? codigo = null, object? datos = null) : base(message)
    {
        StatusCode = statusCode;
        Codigo = codigo;
        Datos = datos;
    }

    public static DomnaException BadRequest(string message, string? codigo = null, object? datos = null) => new(400, message, codigo, datos);
    public static DomnaException Unauthorized(string message, string? codigo = null, object? datos = null) => new(401, message, codigo, datos);
    public static DomnaException NotFound(string message, string? codigo = null, object? datos = null) => new(404, message, codigo, datos);
    public static DomnaException Conflict(string message, string? codigo = null, object? datos = null) => new(409, message, codigo, datos);
}
