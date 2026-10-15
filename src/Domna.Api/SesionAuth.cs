using Domna.Application.Abstractions;
using Domna.Application.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Domna.Api;

/// <summary>
/// Caché de la validación de sesiones por `sid`, respaldada por IMemoryCache. La entrada vive ~10 s
/// (la fija el evento OnTokenValidated); aquí solo exponemos la invalidación para que, al revocar una
/// sesión en esta misma instancia, el 401 sea inmediato sin esperar a que caduque la entrada.
/// </summary>
public class SesionCache : ICacheSesion
{
    private readonly IMemoryCache _cache;

    public SesionCache(IMemoryCache cache) => _cache = cache;

    /// <summary>Llave de caché estable para un `sid`.</summary>
    public static string Llave(string sesionId) => $"sesion:{sesionId}";

    public void Invalidar(string sesionId) => _cache.Remove(Llave(sesionId));
}

/// <summary>Traduce un <see cref="EstadoSesion"/> al código/mensaje que ve el front en los 401.</summary>
public static class SesionCodigos
{
    public static string De(EstadoSesion estado) => estado switch
    {
        EstadoSesion.Reemplazada => "sesion_reemplazada",
        EstadoSesion.Cerrada => "sesion_cerrada",
        EstadoSesion.PinCambiado => "sesion_reemplazada", // el PIN cambió: para el front es "entra de nuevo"
        EstadoSesion.Desactivada => "sesion_cerrada",      // cuenta bloqueada: la sesión ya no vale
        EstadoSesion.Expirada => "sesion_expirada",
        _ => "sesion_invalida",
    };

    public static string Mensaje(string codigo) => codigo switch
    {
        "sesion_reemplazada" => "Tu sesión se abrió en otro dispositivo.",
        "sesion_cerrada" => "Tu sesión se cerró.",
        "sesion_expirada" => "Tu sesión expiró. Inicia sesión de nuevo.",
        "sesion_invalida" => "Tu sesión ya no es válida.",
        _ => "No autenticado.",
    };
}
