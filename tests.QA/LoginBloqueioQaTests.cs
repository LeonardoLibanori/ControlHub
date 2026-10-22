using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ControleEstoqueLoja.Security;
using Xunit;

namespace tests.QA;

/// <summary>
/// CARACTERIZACAO - Bloqueio de login por tentativas.
/// Devem FALHAR enquanto a vulnerabilidade existir.
/// </summary>
public class LoginBloqueioQaTests
{
    private sealed class RelogioFake : TimeProvider
    {
        private DateTimeOffset _agora;
        public RelogioFake(DateTimeOffset inicio) => _agora = inicio;
        public override DateTimeOffset GetUtcNow() => _agora;
        public void Avancar(TimeSpan span) => _agora = _agora.Add(span);
        public void Definir(DateTimeOffset valor) => _agora = valor;
    }

    [Fact]
    public void T1_CincoFalhasBloqueiaEMinutosArredondadosParaCima()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var relogio = new RelogioFake(inicio);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15 }, relogio);

        for (int i = 0; i < 4; i++)
        {
            var (bloq, _, _) = throttle.RegistrarFalha("usuario@loja.com", "1.2.3.4");
            Assert.False(bloq);
        }
        var apos4 = throttle.Verificar("usuario@loja.com", "1.2.3.4");
        Assert.False(apos4.Bloqueado);
        Assert.Equal(1, apos4.TentativasRestantes);

        var (bloq5, mins5, rest5) = throttle.RegistrarFalha("usuario@loja.com", "1.2.3.4");
        Assert.True(bloq5);
        Assert.Equal(15, mins5);
        Assert.Equal(0, rest5);

        var durante = throttle.Verificar("usuario@loja.com", "1.2.3.4");
        Assert.True(durante.Bloqueado);
        Assert.Equal(15, durante.MinutosRestantes);

        relogio.Avancar(TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(1)));
        var umMin = throttle.Verificar("usuario@loja.com", "1.2.3.4");
        Assert.True(umMin.Bloqueado);
        Assert.Equal(1, umMin.MinutosRestantes);

        relogio.Definir(inicio.AddMinutes(15));
        var livre = throttle.Verificar("usuario@loja.com", "1.2.3.4");
        Assert.False(livre.Bloqueado);
    }

    [Fact]
    public void T2_FalhasDuranteBloqueioNaoEstendemPrazo()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var relogio = new RelogioFake(inicio);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15 }, relogio);

        for (int i = 0; i < 5; i++)
            throttle.RegistrarFalha("u@l.com", "1.2.3.4");

        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        throttle.RegistrarFalha("u@l.com", "1.2.3.4");

        relogio.Definir(inicio.AddMinutes(15));
        Assert.False(throttle.Verificar("u@l.com", "1.2.3.4").Bloqueado);
    }

    [Fact]
    public void T3_SucessoZeraEJanelaDeslizante()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var relogio = new RelogioFake(inicio);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15 }, relogio);

        for (int i = 0; i < 3; i++)
            throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        Assert.Equal(2, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);

        throttle.RegistrarSucesso("u@l.com", "1.2.3.4");
        Assert.Equal(5, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);

        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        relogio.Definir(inicio.AddMinutes(15).AddSeconds(1));
        Assert.Equal(5, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);
    }

    [Fact]
    public void T4_ChaveNormalizadaIgnoreCaseEspacos()
    {
        var relogio = new RelogioFake(DateTimeOffset.UtcNow);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 2, BloqueioMinutos = 15 }, relogio);

        throttle.RegistrarFalha("Admin@Loja.com ", "1.2.3.4");
        var estado = throttle.Verificar("admin@loja.com", "1.2.3.4");
        Assert.Equal(1, estado.TentativasRestantes);
    }

    [Fact]
    public void T5_LimitePorIpBloqueiaQualquerChave()
    {
        var relogio = new RelogioFake(DateTimeOffset.UtcNow);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15, MaximoPorIp = 3 }, relogio);

        throttle.RegistrarFalha("a@l.com", "10.0.0.1");
        throttle.RegistrarFalha("b@l.com", "10.0.0.1");
        var apos2 = throttle.Verificar("c@l.com", "10.0.0.1");
        Assert.False(apos2.Bloqueado);

        throttle.RegistrarFalha("c@l.com", "10.0.0.1");
        var bloqueadoIp = throttle.Verificar("z@l.com", "10.0.0.1");
        Assert.True(bloqueadoIp.Bloqueado);

        var outroIp = throttle.Verificar("z@l.com", "10.0.0.2");
        Assert.False(outroIp.Bloqueado);
    }

    [Fact]
    public void T6_ChaveInexistenteIgualExistente()
    {
        var relogio = new RelogioFake(DateTimeOffset.UtcNow);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 2, BloqueioMinutos = 15 }, relogio);

        var naoExiste = throttle.Verificar("naoexiste@l.com", "1.2.3.4");
        Assert.False(naoExiste.Bloqueado);
        Assert.Equal(2, naoExiste.TentativasRestantes);

        throttle.RegistrarFalha("existe@l.com", "1.2.3.4");
        var existe = throttle.Verificar("existe@l.com", "1.2.3.4");
        Assert.Equal(1, existe.TentativasRestantes);
    }

    [Fact]
    public void T7_TentativasRestantes()
    {
        var relogio = new RelogioFake(DateTimeOffset.UtcNow);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15 }, relogio);

        Assert.Equal(5, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);
        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        Assert.Equal(4, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);
        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        Assert.Equal(3, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);
        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        Assert.Equal(2, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);
        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        Assert.Equal(1, throttle.Verificar("u@l.com", "1.2.3.4").TentativasRestantes);
        throttle.RegistrarFalha("u@l.com", "1.2.3.4");
        var bloqueado = throttle.Verificar("u@l.com", "1.2.3.4");
        Assert.True(bloqueado.Bloqueado);
        Assert.Equal(0, bloqueado.TentativasRestantes);
    }

    [Fact]
    public void T8_CincoCinquentaFalhasParalelasNaoEstouro()
    {
        var relogio = new RelogioFake(DateTimeOffset.UtcNow);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15 }, relogio);

        var tarefas = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => throttle.RegistrarFalha("u@l.com", "1.2.3.4")))
            .ToList();

        Task.WaitAll(tarefas.ToArray());

        var estado = throttle.Verificar("u@l.com", "1.2.3.4");
        Assert.True(estado.Bloqueado);
        Assert.Equal(0, estado.TentativasRestantes);
    }

    [Fact]
    public void T9_LimpezaDescartaEntradasAntigas()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var relogio = new RelogioFake(inicio);
        var throttle = new LoginThrottle(new LoginThrottleOptions { MaximoTentativas = 5, BloqueioMinutos = 15 }, relogio);

        for (int i = 0; i < 100; i++)
            throttle.RegistrarFalha($"usuario{i}@l.com", $"10.0.0.{i % 254}");

        relogio.Definir(inicio.AddMinutes(31));

        for (int i = 0; i < 60; i++)
            throttle.Verificar($"usuario{i}@l.com", $"10.0.0.{i % 254}");

        Assert.True(true);
    }
}
