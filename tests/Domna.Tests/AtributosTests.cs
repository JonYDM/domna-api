using Domna.Domain;
using Xunit;

namespace Domna.Tests;

public class AtributosTests
{
    // ── Tallas ──

    [Fact]
    public void NormalizarTallas_pone_codigos_en_mayusculas()
    {
        var r = Atributos.NormalizarTallas(new[] { "ch", "m", "g" });
        Assert.Equal(new[] { "CH", "M", "G" }, r);
    }

    [Fact]
    public void NormalizarTallas_capitaliza_palabras_de_tamano()
    {
        var r = Atributos.NormalizarTallas(new[] { "chica", "mediana", "grande" });
        Assert.Equal(new[] { "Chica", "Mediana", "Grande" }, r);
    }

    [Theory]
    [InlineData("unica")]
    [InlineData("única")]
    [InlineData("ÚNICA")]
    public void NormalizarTallas_unifica_unica(string entrada)
    {
        var r = Atributos.NormalizarTallas(new[] { entrada });
        Assert.Equal(new[] { "Única" }, r);
    }

    [Fact]
    public void NormalizarTallas_quita_repetidas()
    {
        var r = Atributos.NormalizarTallas(new[] { "ch", "CH", " ch " });
        Assert.Equal(new[] { "CH" }, r);
    }

    [Fact]
    public void NormalizarTallas_sin_tallas_lanza_400()
    {
        Assert.Throws<DomnaException>(() => Atributos.NormalizarTallas(new[] { "  ", "" }));
    }

    [Fact]
    public void NormalizarTallas_mas_de_15_lanza_400()
    {
        var muchas = Enumerable.Range(1, 16).Select(i => $"T{i}").ToArray();
        var ex = Assert.Throws<DomnaException>(() => Atributos.NormalizarTallas(muchas));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void NormalizarTallas_codigo_muy_largo_lanza_400()
    {
        // 7 caracteres (máx. 6 para códigos).
        Assert.Throws<DomnaException>(() => Atributos.NormalizarTallas(new[] { "ABCDEFG" }));
    }

    // ── Hex ──

    [Theory]
    [InlineData("#f2a", "#FF22AA")]
    [InlineData("f2a", "#FF22AA")]
    [InlineData("#F2A7B5", "#F2A7B5")]
    [InlineData("f2a7b5", "#F2A7B5")]
    public void NormalizarHex_expande_y_normaliza(string entrada, string esperado)
    {
        Assert.Equal(esperado, Atributos.NormalizarHex(entrada));
    }

    [Theory]
    [InlineData("rojo")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    public void NormalizarHex_rechaza_invalidos(string entrada)
    {
        Assert.Throws<DomnaException>(() => Atributos.NormalizarHex(entrada));
    }

    // ── Familia desde hex ──

    [Theory]
    [InlineData("#1F1B24", FamiliaColor.Negro)]
    [InlineData("#F2A7B5", FamiliaColor.Rosa)]
    [InlineData("#4A6A92", FamiliaColor.Azul)]
    [InlineData("#D9C4A5", FamiliaColor.Beige)]
    public void FamiliaDeHex_clasifica(string hex, FamiliaColor esperada)
    {
        Assert.Equal(esperada, Atributos.FamiliaDeHex(hex));
    }

    // ── MismoNombre ──

    [Theory]
    [InlineData("Vestidos", "vestidos")]
    [InlineData("Blusas / Tops", "blusas-tops")]
    [InlineData("Pantalón", "pantalon")]
    public void MismoNombre_ignora_mayusculas_acentos_y_simbolos(string a, string b)
    {
        Assert.True(Atributos.MismoNombre(a, b));
    }
}
