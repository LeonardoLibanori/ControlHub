using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ControleEstoqueLoja.Controllers;

[Authorize]
public class KardexController : Controller
{
    public const int TamanhoPagina = 50;

    private readonly ApplicationDbContext _context;

    public KardexController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = Policies.PodeGerenciarEstoque)]
    public async Task<IActionResult> Index(
        int? produtoId,
        DateTime? de,
        DateTime? ate,
        TipoMovimentacaoEstoque? tipo,
        string? usuarioId,
        string? origemTipo,
        int? origemId,
        int pagina = 1)
    {
        if (pagina < 1) pagina = 1;

        var consulta = _context.MovimentacoesEstoque.AsNoTracking().AsQueryable();

        if (produtoId.HasValue)
            consulta = consulta.Where(m => m.ProdutoId == produtoId.Value);

        if (de.HasValue)
        {
            var inicio = de.Value.Date;
            consulta = consulta.Where(m => m.DataHora >= inicio);
        }

        if (ate.HasValue)
        {
            var fimExclusivo = ate.Value.Date.AddDays(1);
            consulta = consulta.Where(m => m.DataHora < fimExclusivo);
        }

        if (tipo.HasValue)
            consulta = consulta.Where(m => m.Tipo == tipo.Value);

        if (!string.IsNullOrWhiteSpace(usuarioId))
            consulta = consulta.Where(m => m.UsuarioId == usuarioId);

        if (!string.IsNullOrWhiteSpace(origemTipo))
            consulta = consulta.Where(m => m.OrigemTipo == origemTipo);

        if (origemId.HasValue)
            consulta = consulta.Where(m => m.OrigemId == origemId.Value);

        var total = await consulta.CountAsync();
        var itens = await consulta
            .OrderByDescending(m => m.DataHora)
            .ThenByDescending(m => m.Id)
            .Skip((pagina - 1) * TamanhoPagina)
            .Take(TamanhoPagina)
            .Select(m => new KardexLinhaViewModel
            {
                Id = m.Id,
                DataHora = m.DataHora,
                ProdutoId = m.ProdutoId,
                ProdutoNome = m.Produto != null ? m.Produto.Nome : ("#" + m.ProdutoId),
                Tipo = m.Tipo,
                QuantidadeMovimentada = m.QuantidadeMovimentada,
                SaldoAnterior = m.SaldoAnterior,
                SaldoPosterior = m.SaldoPosterior,
                UsuarioId = m.UsuarioId,
                Motivo = m.Motivo,
                OrigemTipo = m.OrigemTipo,
                OrigemId = m.OrigemId
            })
            .ToListAsync();

        var nomesUsuarios = await NomesUsuariosAsync(itens.Select(i => i.UsuarioId));
        foreach (var linha in itens)
        {
            if (linha.UsuarioId != null && nomesUsuarios.TryGetValue(linha.UsuarioId, out var nome))
                linha.UsuarioNome = nome;
        }

        var produtos = await _context.Produtos.AsNoTracking()
            .Where(p => p.Ativo)
            .OrderBy(p => p.Nome)
            .Select(p => new KardexProdutoOpcao { Id = p.Id, Nome = p.Nome })
            .ToListAsync();

        ViewData["Title"] = "Kardex";
        return View(new KardexPaginaViewModel
        {
            Itens = itens,
            Produtos = produtos,
            ProdutoId = produtoId,
            De = de,
            Ate = ate,
            Tipo = tipo,
            UsuarioId = usuarioId,
            OrigemTipo = origemTipo,
            OrigemId = origemId,
            Pagina = pagina,
            TotalRegistros = total,
            TotalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)TamanhoPagina))
        });
    }

    [HttpGet]
    [Authorize(Policy = Policies.PodeGerenciarEstoque)]
    public async Task<IActionResult> Auditoria(
        DateTime? de,
        DateTime? ate,
        string? acao,
        string? entidade,
        int pagina = 1)
    {
        if (pagina < 1) pagina = 1;

        var consulta = _context.RegistrosAuditoria.AsNoTracking().AsQueryable();
        if (de.HasValue)
            consulta = consulta.Where(a => a.DataHora >= de.Value.Date);
        if (ate.HasValue)
            consulta = consulta.Where(a => a.DataHora < ate.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(acao))
            consulta = consulta.Where(a => a.Acao == acao);
        if (!string.IsNullOrWhiteSpace(entidade))
            consulta = consulta.Where(a => a.Entidade == entidade);

        var total = await consulta.CountAsync();
        var itens = await consulta
            .OrderByDescending(a => a.DataHora)
            .ThenByDescending(a => a.Id)
            .Skip((pagina - 1) * TamanhoPagina)
            .Take(TamanhoPagina)
            .ToListAsync();

        var nomes = await NomesUsuariosAsync(itens.Select(i => i.UsuarioId));
        ViewData["Title"] = "Auditoria";
        ViewBag.De = de;
        ViewBag.Ate = ate;
        ViewBag.Acao = acao;
        ViewBag.Entidade = entidade;
        ViewBag.Pagina = pagina;
        ViewBag.TotalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)TamanhoPagina));
        ViewBag.TotalRegistros = total;
        ViewBag.NomesUsuarios = nomes;
        return View(itens);
    }

    private async Task<Dictionary<string, string>> NomesUsuariosAsync(IEnumerable<string?> ids)
    {
        var distintos = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        if (distintos.Length == 0)
            return new Dictionary<string, string>();

        return await _context.Users.AsNoTracking()
            .Where(u => distintos.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => string.IsNullOrWhiteSpace(u.NomeExibicao) ? (u.Email ?? u.Id) : u.NomeExibicao!);
    }
}

public sealed class KardexPaginaViewModel
{
    public IReadOnlyList<KardexLinhaViewModel> Itens { get; set; } = Array.Empty<KardexLinhaViewModel>();
    public IReadOnlyList<KardexProdutoOpcao> Produtos { get; set; } = Array.Empty<KardexProdutoOpcao>();
    public int? ProdutoId { get; set; }
    public DateTime? De { get; set; }
    public DateTime? Ate { get; set; }
    public TipoMovimentacaoEstoque? Tipo { get; set; }
    public string? UsuarioId { get; set; }
    public string? OrigemTipo { get; set; }
    public int? OrigemId { get; set; }
    public int Pagina { get; set; }
    public int TotalRegistros { get; set; }
    public int TotalPaginas { get; set; }
}

public sealed class KardexLinhaViewModel
{
    public int Id { get; set; }
    public DateTime DataHora { get; set; }
    public int ProdutoId { get; set; }
    public string ProdutoNome { get; set; } = string.Empty;
    public TipoMovimentacaoEstoque Tipo { get; set; }
    public decimal QuantidadeMovimentada { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal SaldoPosterior { get; set; }
    public string? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Motivo { get; set; }
    public string OrigemTipo { get; set; } = string.Empty;
    public int? OrigemId { get; set; }
}

public sealed class KardexProdutoOpcao
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}
