namespace Domna.Domain;

/// <summary>
/// Token de recuperación de PIN por correo. Un solo uso y con expiración (30 min por defecto),
/// como en src/mock/clientas.ts. En producción el token viaja por correo transaccional.
/// </summary>
public class RecuperacionPin
{
    public string Token { get; private set; } = default!;
    public string ClientaId { get; private set; } = default!;
    public DateTime Expira { get; private set; }
    public bool Usado { get; private set; }

    private RecuperacionPin() { }

    public static RecuperacionPin Crear(string token, string clientaId, DateTime ahora, int minutosVigencia = 30)
    {
        return new RecuperacionPin
        {
            Token = token,
            ClientaId = clientaId,
            Expira = ahora.AddMinutes(minutosVigencia),
            Usado = false
        };
    }

    public bool EsValido(DateTime ahora) => !Usado && Expira >= ahora;

    public void Consumir() => Usado = true;
}
