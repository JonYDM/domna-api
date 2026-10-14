namespace Domna.Domain;

/// <summary>CLABE interbancaria mexicana: 18 dígitos, el último es un dígito verificador.</summary>
public static class Clabe
{
    private static readonly int[] Pesos = { 3, 7, 1 };

    /// <summary>Quita espacios y guiones. No valida.</summary>
    public static string Limpiar(string? valor) =>
        new string((valor ?? "").Where(char.IsLetterOrDigit).ToArray());

    public static bool EsValida(string? valor)
    {
        var c = Limpiar(valor);
        if (c.Length != 18 || !c.All(char.IsAsciiDigit)) return false;

        var suma = 0;
        for (var i = 0; i < 17; i++)
            suma += (c[i] - '0') * Pesos[i % 3] % 10;
        var verificador = (10 - suma % 10) % 10;
        return verificador == c[17] - '0';
    }
}
