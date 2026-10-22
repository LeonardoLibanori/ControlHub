using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace ControleEstoqueLoja.Security;

/// <summary>
/// Throttle de tentativas de login em memória.
/// Chave: e-mail/usuário normalizado (Trim + ToLowerInvariant).
/// Contador separado por IP.
/// Nenhum I/O, nenhum log de senha.
/// </summary>
public sealed class LoginThrottle
{
    private readonly LoginThrottleOptions _opcoes;
    private readonly TimeProvider _relogio;
    private readonly ConcurrentDictionary<string, RegistroChave> _chaves = new();
    private readonly ConcurrentDictionary<string, RegistroIp> _ips = new();
    private long _chamadasAteLimpeza;

    public LoginThrottle(LoginThrottleOptions opcoes, TimeProvider? relogio = null)
    {
        _opcoes = opcoes ?? new LoginThrottleOptions();
        _relogio = relogio ?? TimeProvider.System;
    }

    /// <summary>Verifica se a chave/IP está bloqueada, sem alterar contadores.</summary>
    public (bool Bloqueado, int MinutosRestantes, int TentativasRestantes) Verificar(string chave, string ip)
    {
        var agora = _relogio.GetUtcNow();
        var chaveNorm = NormalizarChave(chave);

        if (EstaBloqueado(chaveNorm, agora, out var minutosChave, out var restantesChave))
            return (true, minutosChave, restantesChave);

        if (EstaBloqueadoIp(ip, agora, out var minutosIp, out var restantesIp))
            return (true, minutosIp, Math.Min(restantesIp, restantesChave));

        return (false, 0, Math.Max(0, _opcoes.MaximoTentativas - ContagemChave(chaveNorm, agora)));
    }

    /// <summary>Registra uma falha e devolve o estado atualizado.</summary>
    public (bool Bloqueado, int MinutosRestantes, int TentativasRestantes) RegistrarFalha(string chave, string ip)
    {
        var agora = _relogio.GetUtcNow();
        var chaveNorm = NormalizarChave(chave);

        var regChave = _chaves.GetOrAdd(chaveNorm, _ => new RegistroChave());
        lock (regChave)
        {
            regChave.Falhas.Add(agora);
            var contagemChave = ContagemFalhasNaJanela(regChave.Falhas, agora);
            if (contagemChave >= _opcoes.MaximoTentativas)
                regChave.BloqueadoAte = agora.AddMinutes(_opcoes.BloqueioMinutos);
        }

        var regIp = _ips.GetOrAdd(ip, _ => new RegistroIp());
        lock (regIp)
        {
            regIp.Falhas.Add(agora);
            var contagemIp = ContagemFalhasNaJanela(regIp.Falhas, agora);
            if (contagemIp >= _opcoes.MaximoPorIp)
                regIp.BloqueadoAte = agora.AddMinutes(_opcoes.BloqueioMinutos);
        }

        LimpezaPreguiçosa();

        return Verificar(chaveNorm, ip);
    }

    /// <summary>Zera o contador da chave após login bem-sucedido.</summary>
    public void RegistrarSucesso(string chave, string ip)
    {
        var chaveNorm = NormalizarChave(chave);
        if (_chaves.TryGetValue(chaveNorm, out var reg))
        {
            lock (reg) { reg.Falhas.Clear(); }
        }
    }

    private static string NormalizarChave(string chave) =>
        (chave ?? string.Empty).Trim().ToLowerInvariant();

    private bool EstaBloqueado(string chaveNorm, DateTimeOffset agora, out int minutos, out int restantes)
    {
        minutos = 0;
        restantes = 0;

        if (!_chaves.TryGetValue(chaveNorm, out var reg))
            return false;

        lock (reg)
        {
            var bloqueio = reg.BloqueadoAte;
            if (bloqueio is null || agora >= bloqueio.Value)
                return false;

            minutos = (int)Math.Ceiling((bloqueio.Value - agora).TotalMinutes);
            if (minutos < 1) minutos = 1;
            restantes = Math.Max(0, _opcoes.MaximoTentativas - ContagemFalhasNaJanela(reg.Falhas, agora));
            return true;
        }
    }

    private bool EstaBloqueadoIp(string ip, DateTimeOffset agora, out int minutos, out int restantes)
    {
        minutos = 0;
        restantes = 0;

        if (!_ips.TryGetValue(ip, out var reg))
            return false;

        lock (reg)
        {
            var bloqueio = reg.BloqueadoAte;
            if (bloqueio is null || agora >= bloqueio.Value)
                return false;

            minutos = (int)Math.Ceiling((bloqueio.Value - agora).TotalMinutes);
            if (minutos < 1) minutos = 1;
            return true;
        }
    }

    private int ContagemChave(string chaveNorm, DateTimeOffset agora)
    {
        if (!_chaves.TryGetValue(chaveNorm, out var reg))
            return 0;

        lock (reg)
        {
            return ContagemFalhasNaJanela(reg.Falhas, agora);
        }
    }

    private int ContagemFalhasNaJanela(List<DateTimeOffset> falhas, DateTimeOffset agora)
    {
        var janelaInicial = agora.AddMinutes(-_opcoes.BloqueioMinutos);
        return falhas.Count(f => f >= janelaInicial);
    }

    private void LimpezaPreguiçosa()
    {
        if (Interlocked.Increment(ref _chamadasAteLimpeza) % 50 != 0)
            return;

        var agora = _relogio.GetUtcNow();
        var limite = agora.AddMinutes(-_opcoes.BloqueioMinutos * 2);

        foreach (var kvp in _chaves)
        {
            lock (kvp.Value)
            {
                kvp.Value.Falhas.RemoveAll(f => f < limite);
                if (kvp.Value.Falhas.Count == 0 && (kvp.Value.BloqueadoAte is null || agora >= kvp.Value.BloqueadoAte.Value))
                    _chaves.TryRemove(kvp.Key, out _);
            }
        }

        foreach (var kvp in _ips)
        {
            lock (kvp.Value)
            {
                kvp.Value.Falhas.RemoveAll(f => f < limite);
                if (kvp.Value.Falhas.Count == 0 && (kvp.Value.BloqueadoAte is null || agora >= kvp.Value.BloqueadoAte.Value))
                    _ips.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed class RegistroChave
    {
        public readonly List<DateTimeOffset> Falhas = new();
        public DateTimeOffset? BloqueadoAte;
    }

    private sealed class RegistroIp
    {
        public readonly List<DateTimeOffset> Falhas = new();
        public DateTimeOffset? BloqueadoAte;
    }
}

public sealed class LoginThrottleOptions
{
    public int MaximoTentativas { get; set; } = 5;
    public int BloqueioMinutos { get; set; } = 15;
    public int MaximoPorIp { get; set; } = 20;
}