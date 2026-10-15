using Domna.Application.Abstractions;

namespace Domna.Application.Services;

/// <summary>
/// Límite de intentos en memoria: 5 fallos dentro de 10 minutos bloquean 10 minutos. Vive en el
/// proceso (se reinicia con un deploy), lo que basta para frenar a quien adivine un PIN en una
/// instancia; si el API escala a varias instancias habría que moverlo a un almacén compartido.
/// </summary>
public class LimitadorIntentos : ILimitadorIntentos
{
    public const int MaxFallos = 5;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Castigo = TimeSpan.FromMinutes(10);

    private sealed class Registro
    {
        public int Fallos;
        public DateTime Desde;
        public DateTime? BloqueadoHasta;
    }

    private readonly Dictionary<string, Registro> _registros = new();
    private readonly object _candado = new();
    private readonly IReloj _reloj;

    public LimitadorIntentos(IReloj reloj) => _reloj = reloj;

    public TimeSpan? Bloqueo(string clave)
    {
        lock (_candado)
        {
            if (!_registros.TryGetValue(clave, out var r) || r.BloqueadoHasta is null) return null;
            var restante = r.BloqueadoHasta.Value - _reloj.Ahora;
            if (restante > TimeSpan.Zero) return restante;
            _registros.Remove(clave); // el castigo ya pasó
            return null;
        }
    }

    public int RegistrarFallo(string clave, int maxFallos = MaxFallos, TimeSpan? castigo = null)
    {
        lock (_candado)
        {
            var ahora = _reloj.Ahora;
            if (!_registros.TryGetValue(clave, out var r) || ahora - r.Desde > Ventana)
            {
                r = new Registro { Desde = ahora };
                _registros[clave] = r;
            }
            r.Fallos++;
            if (r.Fallos >= maxFallos)
            {
                r.BloqueadoHasta = ahora + (castigo ?? Castigo);
                return 0;
            }
            return maxFallos - r.Fallos;
        }
    }

    public void Reiniciar(string clave)
    {
        lock (_candado) _registros.Remove(clave);
    }
}
