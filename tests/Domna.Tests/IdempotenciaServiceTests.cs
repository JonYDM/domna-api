using Domna.Application.Services;
using Domna.Domain;
using Xunit;

namespace Domna.Tests;

/// <summary>
/// Lógica reutilizable de idempotencia: validación del formato de la llave, no re-ejecución dentro
/// de 24 h, repetición de la respuesta 2xx, acotación por usuario+ruta, 409 si sigue en curso,
/// solo-2xx se cachea, límite del cuerpo y limpieza perezosa de registros viejos.
/// </summary>
public class IdempotenciaServiceTests
{
    private readonly RelojFijo _reloj = new();
    private readonly IdempotenciaRepoFake _repo = new();
    private readonly IdempotenciaService _svc;

    private const string Usuario = "u1";
    private const string Ruta = "/api/apartados";
    private const string Llave = "abc123xyz";

    public IdempotenciaServiceTests() => _svc = new IdempotenciaService(_repo, _reloj);

    [Fact]
    public async Task Sin_llave_no_aplica_idempotencia_y_continua()
    {
        var inicio = await _svc.IniciarAsync(Usuario, Ruta, null);
        Assert.Equal(ResultadoIdempotencia.Continuar, inicio.Resultado);
    }

    [Theory]
    [InlineData("corta")]            // < 8
    [InlineData("con espacio")]       // caracter inválido
    [InlineData("tiene/barra")]       // caracter inválido
    [InlineData("añoinvalido123")]    // acento no permitido
    public async Task Una_llave_mal_formada_responde_400(string llave)
    {
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.IniciarAsync(Usuario, Ruta, llave));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Una_llave_demasiado_larga_responde_400()
    {
        var larga = new string('a', 65);
        var ex = await Assert.ThrowsAsync<DomnaException>(() => _svc.IniciarAsync(Usuario, Ruta, larga));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Una_llave_nueva_continua_y_queda_reservada_en_curso()
    {
        var inicio = await _svc.IniciarAsync(Usuario, Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.Continuar, inicio.Resultado);

        var reservado = await _repo.ObtenerAsync(Usuario, Ruta, Llave);
        Assert.NotNull(reservado);
        Assert.False(reservado!.Terminada); // en curso, todavía sin respuesta
    }

    [Fact]
    public async Task Mientras_la_primera_sigue_en_curso_la_segunda_responde_en_curso()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);     // reserva en curso, no completa
        var segunda = await _svc.IniciarAsync(Usuario, Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.EnCurso, segunda.Resultado);
    }

    [Fact]
    public async Task Tras_completar_un_2xx_la_misma_llave_repite_la_misma_respuesta()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 200, "{\"folio\":\"DOM-0001\"}");

        var repite = await _svc.IniciarAsync(Usuario, Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.Repetir, repite.Resultado);
        Assert.Equal(200, repite.CodigoEstado);
        Assert.Equal("{\"folio\":\"DOM-0001\"}", repite.Cuerpo);
    }

    [Fact]
    public async Task Un_fallo_no_se_cachea_y_la_misma_llave_se_puede_reintentar()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 400, "{\"mensaje\":\"error\"}");

        // El registro en curso se borró: la llave vuelve a estar libre.
        Assert.Null(await _repo.ObtenerAsync(Usuario, Ruta, Llave));

        var reintento = await _svc.IniciarAsync(Usuario, Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.Continuar, reintento.Resultado);
    }

    [Fact]
    public async Task La_misma_llave_en_otra_ruta_no_colisiona()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 200, "a");

        var otraRuta = await _svc.IniciarAsync(Usuario, "/api/stock/mover", Llave);
        Assert.Equal(ResultadoIdempotencia.Continuar, otraRuta.Resultado);
    }

    [Fact]
    public async Task La_misma_llave_de_otro_usuario_no_colisiona()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 200, "a");

        var otroUsuario = await _svc.IniciarAsync("u2", Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.Continuar, otroUsuario.Resultado);
    }

    [Fact]
    public async Task Un_cuerpo_demasiado_grande_no_se_cachea()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        var enorme = new string('x', IdempotenciaService.MaxCuerpoBytes + 1);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 200, enorme);

        // No se guardó: la reserva se borró y la llave queda libre.
        Assert.Null(await _repo.ObtenerAsync(Usuario, Ruta, Llave));
        var reintento = await _svc.IniciarAsync(Usuario, Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.Continuar, reintento.Resultado);
    }

    [Fact]
    public async Task Pasadas_24_horas_la_llave_caduca_y_se_puede_volver_a_usar()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 200, "a");

        // Avanza más de 24 h: la limpieza perezosa borra el registro y se puede re-ejecutar.
        _reloj.Ahora = _reloj.Ahora.AddHours(25);
        var tras = await _svc.IniciarAsync(Usuario, Ruta, Llave);

        Assert.Equal(1, _repo.UltimosLimpiados);
        Assert.Equal(ResultadoIdempotencia.Continuar, tras.Resultado);
    }

    [Fact]
    public async Task Dentro_de_las_24_horas_la_llave_sigue_repitiendo()
    {
        await _svc.IniciarAsync(Usuario, Ruta, Llave);
        await _svc.CompletarAsync(Usuario, Ruta, Llave, 201, "cuerpo");

        _reloj.Ahora = _reloj.Ahora.AddHours(23);
        var tras = await _svc.IniciarAsync(Usuario, Ruta, Llave);
        Assert.Equal(ResultadoIdempotencia.Repetir, tras.Resultado);
        Assert.Equal(201, tras.CodigoEstado);
    }

    [Fact]
    public void El_validador_de_formato_acepta_e_rechaza_segun_el_contrato()
    {
        Assert.True(IdempotenciaService.LlaveValida("12345678"));          // 8, mínimo
        Assert.True(IdempotenciaService.LlaveValida("A_b-123456"));         // letras, dígitos, _ y -
        Assert.True(IdempotenciaService.LlaveValida(new string('a', 64)));  // 64, máximo
        Assert.False(IdempotenciaService.LlaveValida("1234567"));           // 7, muy corta
        Assert.False(IdempotenciaService.LlaveValida(new string('a', 65))); // 65, muy larga
        Assert.False(IdempotenciaService.LlaveValida("con espacio"));       // caracter inválido
        Assert.False(IdempotenciaService.LlaveValida("acentué!!"));         // caracteres inválidos
    }
}
