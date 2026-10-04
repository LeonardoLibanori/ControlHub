using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControleEstoqueLoja.Controllers
{
    /// <summary>
    /// ETAPA B - Dashboard: painel de leitura para o dono conferir o dia.
    ///
    /// SO LEITURA, de proposito: nao ha POST/PUT/DELETE aqui, e o teste de
    /// reflexao (Dashboard_Index_EUmGetReadOnly) trava isso. Erro nao e
    /// capturado nesta classe: quem trata e o handler global do Program.cs,
    /// que nao devolve ex.Message ao cliente (regra do BUG-12).
    ///
    /// DINHEIRO: TODA soma e feita em decimal, EM C#, sobre listas ja
    /// materializadas com AsNoTracking + ToListAsync. Motivo: Quantidade vive
    /// em TEXT no SQLite e a collation BINARY ordenaria "10" antes de "9"
    /// (regra 2). Materializar deixa todos os filtros com a MESMA semantica
    /// do caixa.
    ///
    /// "HOJE" E INTERVALO, NAO .Date: [Today, Today + 1 dia). O padrao .Date
    /// vira DATE() no SQLite e depende de fuso; o intervalo nao. Hora local
    /// do servidor, como o resto do sistema (DataVenda e DateTime.Now).
    ///
    /// TotalVenda SEMPRE gravado, nunca recalculado pelos itens: o caixa pode
    /// fechar com desconto no balcao, e recalcular mostraria um numero que
    /// nunca bate com o cupom.
    /// </summary>
    [Authorize(Policy = Policies.PodeVerFinanceiro)]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        /// <summary>Quantos produtos abaixo do minimo a lista mostra.</summary>
        private const int TopEstoqueBaixo = 5;

        /// <summary>Quantas vendas recentes a lista mostra.</summary>
        private const int TopVendasRecentes = 5;

        /// <summary>Janela da serie de vendas (7 dias, hoje incluido).</summary>
        private const int DiasNaSerie = 7;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var inicioHoje = DateTime.Today;
            var fimHoje = inicioHoje.AddDays(1);

            // ---- Vendas de hoje (qualquer forma de pagamento, fechadas inclusive) ----
            var vendasHoje = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= inicioHoje && v.DataVenda < fimHoje)
                .ToListAsync();

            var totalHoje = vendasHoje.Sum(v => v.TotalVenda);
            var numeroVendas = vendasHoje.Count;
            var ticketMedio = numeroVendas == 0
                ? 0m
                : Math.Round(totalHoje / numeroVendas, 2, MidpointRounding.AwayFromZero);

            var porMetodo = MontarPorMetodo(vendasHoje);

            // ---- Gaveta (regra 7) ----
            var movimentosHoje = await _context.CaixaMovimentacoes
                .AsNoTracking()
                .Where(m => m.Data >= inicioHoje && m.Data < fimHoje && !m.IsFechado)
                .ToListAsync();

            var vendasDinheiro = vendasHoje
                .Where(v => !v.IsFechado
                            && v.MetodoPagamento == CaixaController.MetodoPagamentoDinheiro)
                .Sum(v => v.TotalVenda);

            var suprimentos = movimentosHoje.Where(m => m.Tipo == "Suprimento").Sum(m => m.Valor);
            var sangrias = movimentosHoje.Where(m => m.Tipo == "Sangria").Sum(m => m.Valor);

            // Fonte UNICA da formula: nao duplicar, senao os dois numeros divergem.
            var saldoGaveta = CaixaController.CalcularSaldoEmGaveta(vendasDinheiro, suprimentos, sangrias);

            var (estoqueBaixo, totalEstoqueBaixo) = await MontarEstoqueBaixoAsync();

            var ultimasVendas = await _context.Vendas
                .AsNoTracking()
                .OrderByDescending(v => v.DataVenda)
                .ThenByDescending(v => v.Id)
                .Take(TopVendasRecentes)
                .Select(v => new DashboardVendaRecente
                {
                    Id = v.Id,
                    DataVenda = v.DataVenda,
                    MetodoPagamento = v.MetodoPagamento,
                    Total = v.TotalVenda,
                })
                .ToListAsync();

            var serie = await MontarSerieAsync(inicioHoje);

            return View(new DashboardViewModel
            {
                TotalVendasHoje = totalHoje,
                NumeroVendasHoje = numeroVendas,
                TicketMedio = ticketMedio,
                TotalPorMetodo = porMetodo,
                SaldoGaveta = saldoGaveta,
                SerieUltimos7Dias = serie,
                EstoqueBaixo = estoqueBaixo,
                TotalEstoqueBaixo = totalEstoqueBaixo,
                UltimasVendas = ultimasVendas,
            });
        }

        /// <summary>
        /// Agrupa por forma de pagamento SEM diferenciar maiusculas, mostrando
        /// o primeiro nome encontrado ("Dinheiro" e "dinheiro" viram um so
        /// grupo) e ordenando pelo maior valor.
        /// </summary>
        private static List<DashboardTotalPorMetodo> MontarPorMetodo(List<Venda> vendasHoje)
            => vendasHoje
                .GroupBy(v => v.MetodoPagamento, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DashboardTotalPorMetodo
                {
                    Metodo = g.First().MetodoPagamento,
                    Total = g.Sum(v => v.TotalVenda),
                })
                .OrderByDescending(m => m.Total)
                .ToList();

        /// <summary>
        /// Estoque baixo SEMPRE em C#: <see cref="Produto.EstoqueAbaixoDoMinimo"/>
        /// e [NotMapped] e existe justamente porque comparar Quantidade em SQL
        /// daria resultado errado (regra 2). Trazemos os ativos e filtramos aqui.
        ///
        /// Ordenacao: mais critico primeiro, pela razao Quantidade/EstoqueMinimo
        /// menor - produto zerado tem razao 0 e abre a lista. Desempate pelo nome,
        /// para a tela nao "pular" entre renders.
        /// </summary>
        private async Task<(List<DashboardEstoqueBaixo> Lista, int Total)> MontarEstoqueBaixoAsync()
        {
            var ativos = await _context.Produtos
                .AsNoTracking()
                .Where(p => p.Ativo)
                .ToListAsync();

            var abaixo = ativos.Where(p => p.EstoqueAbaixoDoMinimo).ToList();

            var lista = abaixo
                .OrderBy(p => p.Quantidade / p.EstoqueMinimo!.Value)
                .ThenBy(p => p.Nome, StringComparer.Ordinal)
                .Take(TopEstoqueBaixo)
                .Select(p => new DashboardEstoqueBaixo
                {
                    ProdutoId = p.Id,
                    Nome = p.Nome,
                    Quantidade = p.Quantidade,
                    EstoqueMinimo = p.EstoqueMinimo!.Value,
                    PorPeso = p.Tipo == TipoProduto.PorPeso,
                })
                .ToList();

            return (lista, abaixo.Count);
        }

        /// <summary>
        /// Janela de 7 dias em ordem CRONOLOGICA, sempre com os 7 pontos: um dia
        /// sem venda entra com 0, senao o grafico de barras fica torto.
        ///
        /// Consulta PROPRIA: a lista de vendas de hoje so tem o dia de hoje, e
        /// reaproveitar dela deixaria 6 dos 7 pontos zerados.
        /// </summary>
        private async Task<List<DashboardDiaSerie>> MontarSerieAsync(DateTime inicioHoje)
        {
            var inicioJanela = inicioHoje.AddDays(-(DiasNaSerie - 1));
            var fimJanela = inicioHoje.AddDays(1);

            var vendasDaJanela = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= inicioJanela && v.DataVenda < fimJanela)
                .ToListAsync();

            var totalPorDia = vendasDaJanela
                .GroupBy(v => v.DataVenda.Date)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.TotalVenda));

            var serie = new List<DashboardDiaSerie>(DiasNaSerie);
            for (var i = 0; i < DiasNaSerie; i++)
            {
                var dia = inicioJanela.AddDays(i);
                serie.Add(new DashboardDiaSerie
                {
                    Dia = dia,
                    Total = totalPorDia.TryGetValue(dia, out var total) ? total : 0m,
                });
            }

            return serie;
        }
    }

    // =====================================================================
    // TIPOS DO VIEW-MODEL
    // =====================================================================

    /// <summary>Total vendido hoje, agrupado por metodo de pagamento.</summary>
    public class DashboardTotalPorMetodo
    {
        public string Metodo { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>Um dia da serie dos ultimos 7 dias (dia sem venda = 0).</summary>
    public class DashboardDiaSerie
    {
        public DateTime Dia { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>
    /// Um produto abaixo do estoque minimo. Repete Quantidade/EstoqueMinimo
    /// para a view nao precisar carregar o Produto inteiro.
    /// </summary>
    public class DashboardEstoqueBaixo
    {
        public int ProdutoId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public decimal EstoqueMinimo { get; set; }

        /// <summary>
        /// Derivacao EXISTENTE (regras 19 e 20): PorPeso e venda por peso, o
        /// que na pratica significa kg. Nao ha campo de unidade de medida no
        /// modelo - ver LACUNAS no ESTADO-DO-PROJETO.md.
        /// </summary>
        public bool PorPeso { get; set; }

        /// <summary>Texto da unidade para a tela: "kg" ou "un".</summary>
        public string Unidade => PorPeso ? "kg" : "un";

        /// <summary>
        /// Quanto falta para sair do vermelho, na MESMA unidade da quantidade.
        /// </summary>
        public decimal Faltam => Math.Max(0m, EstoqueMinimo - Quantidade);
    }

    /// <summary>Uma venda do historico recente (a tela mostra hora e total).</summary>
    public class DashboardVendaRecente
    {
        public int Id { get; set; }
        public DateTime DataVenda { get; set; }
        public string MetodoPagamento { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>Tudo que a tela do Dashboard precisa, em um objeto so.</summary>
    public class DashboardViewModel
    {
        /// <summary>Soma de TotalVenda GRAVADO das vendas de hoje (qualquer metodo, fechadas inclusive).</summary>
        public decimal TotalVendasHoje { get; set; }

        /// <summary>Quantas vendas occurred hoje (para o ticket medio).</summary>
        public int NumeroVendasHoje { get; set; }

        /// <summary>Total / numero, arredondado em 2 casas, AwayFromZero.</summary>
        public decimal TicketMedio { get; set; }

        /// <summary>Soma por metodo, com as chaves normalizadas (caixa alta).</summary>
        public List<DashboardTotalPorMetodo> TotalPorMetodo { get; set; } = new();

        /// <summary>
        /// Saldo fisico da gaveta: vendas em Dinheiro NAO fechadas de hoje +
        /// suprimentos + sangrias. Sangria ja e negativa no banco e por isso
        /// SOMA (fonte unica: CaixaController.CalcularSaldoEmGaveta).
        /// </summary>
        public decimal SaldoGaveta { get; set; }

        /// <summary>7 dias em ordem cronologica (hoje por ultimo).</summary>
        public List<DashboardDiaSerie> SerieUltimos7Dias { get; set; } = new();

        /// <summary>Produtos abaixo do minimo, os mais criticos primeiro (no maximo 5).</summary>
        public List<DashboardEstoqueBaixo> EstoqueBaixo { get; set; } = new();

        /// <summary>Quantos produtos estao abaixo do minimo NO TOTAL (a lista mostra so 5).</summary>
        public int TotalEstoqueBaixo { get; set; }

        /// <summary>As 5 vendas mais recentes, de qualquer dia.</summary>
        public List<DashboardVendaRecente> UltimasVendas { get; set; } = new();
    }
}