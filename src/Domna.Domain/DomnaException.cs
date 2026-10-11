namespace Domna.Domain;

/// <summary>
/// Error de negocio con un código HTTP asociado. Equivale al ApiError del front
/// (src/lib/errores.ts): 400 validación, 401 credenciales, 404 no encontrado, 409 conflicto.
/// El middleware de la Api lo traduce a la respuesta HTTP correspondiente.
/// </summary>
public sealed class DomnaException : Exception
{
    public int StatusCode { get; }

    public DomnaException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static DomnaException BadRequest(string message) => new(400, message);
    public static DomnaException Unauthorized(string message) => new(401, message);
    public static DomnaException NotFound(string message) => new(404, message);
    public static DomnaException Conflict(string message) => new(409, message);
}
