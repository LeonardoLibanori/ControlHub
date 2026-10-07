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

        /// <summary>Janelas aceitas no grafico. Qualquer outro valor cai em 7.</summary>
        private const int DiasPadrao = 7;
        private const int DiasLongo = 30;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? dias = null, DateTime? data = null)
        {
            // So 7 e 30 valem. Tudo mais (vazio, 0, -1, 15, 999) volta para 7.
            var janela = NormalizarDias(dias);

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

            // ---- Total da JANELA (7 ou 30 dias), em TotalVenda gravado.
            // Projecao: so (DataVenda, TotalVenda), nunca os itens da venda.
            var inicioJanela = inicioHoje.AddDays(-(janela - 1));
            var vendasDaJanela = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= inicioJanela && v.DataVenda < fimHoje)
                .Select(v => new VendaPeriodo { Data = v.DataVenda, Total = v.TotalVenda })
                .ToListAsync();

            var totalPeriodo = vendasDaJanela.Sum(v => v.Total);
            var numeroPeriodo = vendasDaJanela.Count;

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

            var serie = MontarSerie(vendasDaJanela, inicioHoje, janela);

            // ---- VENDAS DE UM DIA (?data= = clique numa barra do grafico) ----
            // O clique leva para o PROPRIO painel: a lista do dia aparece logo
            // abaixo do grafico, sem abrir a tela Dia. data nula, invalida ou
            // posterior a HOJE nao seleciona nada - nao ha venda no futuro
            // (mesma regra da tela Dia). A comparacao com inicioHoje vem ANTES
            // do AddDays, entao o passo nunca passa de DateTime.MaxValue.
            DateTime? diaSelecionado = null;
            var vendasDoDia = new List<DashboardVendaRecente>();
            var totalDiaSelecionado = 0m;

            if (data.HasValue && data.Value.Date <= inicioHoje)
            {
                var dia = data.Value.Date;
                var fimDia = dia.AddDays(1);

                vendasDoDia = await _context.Vendas
                    .AsNoTracking()
                    .Where(v => v.DataVenda >= dia && v.DataVenda < fimDia)
                    .OrderByDescending(v => v.DataVenda)
                    .ThenByDescending(v => v.Id)
                    .Select(v => new DashboardVendaRecente
                    {
                        Id = v.Id,
                        DataVenda = v.DataVenda,
                        MetodoPagamento = v.MetodoPagamento,
                        Total = v.TotalVenda,
                    })
                    .ToListAsync();

                totalDiaSelecionado = vendasDoDia.Sum(v => v.Total);
                diaSelecionado = dia;
            }

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
                TotalVendasPeriodo = totalPeriodo,
                NumeroVendasPeriodo = numeroPeriodo,
                DiaSelecionado = diaSelecionado,
                VendasDoDia = vendasDoDia,
                TotalDiaSelecionado = totalDiaSelecionado,
                NumeroVendasDia = vendasDoDia.Count,

                Dias = janela,
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
        /// So 7 e 30 sao janelas validas. Vazio, 0, negativo, 15, 999 ou
        /// qualquer coisa fora disso volta para 7 - um parametro de URL nao
        /// pode escolher uma janela qualquer e estourar a consulta.
        /// </summary>
        private static int NormalizarDias(int? dias)
            => dias == DiasLongo ? DiasLongo : DiasPadrao;

        /// <summary>
        /// Monta a serie da janela em ordem CRONOLOGICA, sempre com N pontos
        /// (dia sem venda entra com 0, senao o grafico fica torto) e calcula a
        /// altura de cada barra em PORCENTAGEM.
        ///
        /// Altura: maior = 100; um dia com venda NUNCA abaixo de 4% - sem esse
        /// piso, um dia de R$ 9,98 contra um maximo de R$ 1.000 daria ~1% e a
        /// barra arredondaria para 0px, sumindo do grafico; zero continua 0;
        /// com todos os dias zerados o divisor e 1 (nada de divisao por zero).
        /// </summary>
        private static List<DashboardDiaSerie> MontarSerie(
            List<VendaPeriodo> vendas, DateTime inicioHoje, int janela)
        {
            var inicioJanela = inicioHoje.AddDays(-(janela - 1));

            var totalPorDia = vendas
                .GroupBy(v => v.Data.Date)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Total));

            var serie = new List<DashboardDiaSerie>(janela);
            for (var i = 0; i < janela; i++)
            {
                var dia = inicioJanela.AddDays(i);
                serie.Add(new DashboardDiaSerie
                {
                    Dia = dia,
                    Total = totalPorDia.TryGetValue(dia, out var total) ? total : 0m,
                });
            }

            // Altura: maior primeiro. Divisor 1 quando tudo e zero evita NaN.
            var maior = serie.Count == 0 ? 0m : serie.Max(d => d.Total);
            var divisor = maior > 0m ? maior : 1m;

            foreach (var dia in serie)
            {
                if (dia.Total <= 0m)
                {
                    dia.PercentualAltura = 0m;
                    continue;
                }

                var percentual = dia.Total / divisor * 100m;
                // Piso de 4%: com venda, a barra SEMPRE aparece.
                dia.PercentualAltura = percentual < 4m ? 4m : Math.Round(percentual, 2);
            }

            return serie;
        }

        // =====================================================================
        // DETALHE DE UM DIA
        // =====================================================================

        /// <summary>
        /// Detalhe das vendas de UM dia. Leitura pura (regra 25), herda a
        /// policy da classe.
        ///
        /// DATA NULA, INVALIDA OU POSTERIOR A HOJE volta para o painel: nao ha
        /// venda no futuro. Datas passadas quaisquer sao aceitas - e o AddDays
        /// para o fim do dia nunca roda sobre DateTime.MaxValue, porque a data
        /// ja foi comparada com DateTime.Today antes.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Dia(DateTime? data)
        {
            var hoje = DateTime.Today;

            if (!data.HasValue || data.Value.Date > hoje)
            {
                return RedirectToAction(nameof(Index));
            }

            var dia = data.Value.Date;
            var fim = dia.AddDays(1);

            // Projecao: so os 4 campos do cabecalho da lista de vendas.
            var vendas = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= dia && v.DataVenda < fim)
                .OrderByDescending(v => v.DataVenda)
                .ThenByDescending(v => v.Id)
                .Select(v => new DashboardVendaRecente
                {
                    Id = v.Id,
                    DataVenda = v.DataVenda,
                    MetodoPagamento = v.MetodoPagamento,
                    Total = v.TotalVenda,
                })
                .ToListAsync();

            var total = vendas.Sum(v => v.Total);
            var numero = vendas.Count;
            var ticket = numero == 0
                ? 0m
                : Math.Round(total / numero, 2, MidpointRounding.AwayFromZero);

            var produtos = await MontarProdutosDoDiaAsync(dia, fim);

            return View(new DashboardDiaViewModel
            {
                Dia = dia,
                Total = total,
                NumeroVendas = numero,
                TicketMedio = ticket,
                Produtos = produtos,
                Vendas = vendas,
            });
        }

/// <summary>
        /// "O que foi vendido": agrupa por produto e soma Quantidade EM C#.
        ///
        /// POR QUE EM C# E NAO EM SQL: Quantidade e decimal em TEXT no SQLite e
        /// a collation BINARY ordenaria "10" antes de "9" (regra 2). Trazemos os
        /// itens e somamos em decimal, onde 9 + 10 = 19 e 0,125 + 0,375 = 0,5.
        ///
        /// Produto desativado HOJE aparece: ele vendeu no dia, e esconder o que
        /// foi vendido seria mentira.
        /// </summary>
        private async Task<List<DashboardDiaProduto>> MontarProdutosDoDiaAsync(DateTime dia, DateTime fim)
        {
            var itens = await _context.VendaItens
                .AsNoTracking()
                .Include(i => i.Produto)
                .Where(i => i.Venda.DataVenda >= dia && i.Venda.DataVenda < fim)
                .Select(i => new
                {
                    i.ProdutoId,
                    Nome = i.Produto.Nome,
                    Tipo = i.Produto.Tipo,
                    i.Quantidade,
                    i.PrecoUnitario,
                })
                .ToListAsync();

            return itens
                .GroupBy(i => i.ProdutoId)
                .Select(g =>
                {
                    // Soma em decimal: o Arredondamento e SÓ no valor final.
                    var quantidade = g.Sum(i => i.Quantidade);
                    var valor = Math.Round(g.Sum(i => i.PrecoUnitario * i.Quantidade), 2);

                    return new DashboardDiaProduto
                    {
                        ProdutoId = g.Key,
                        Nome = g.First().Nome,
                        Quantidade = quantidade,
                        Valor = valor,
                        PorPeso = g.First().Tipo == TipoProduto.PorPeso,
                    };
                })
                .OrderByDescending(p => p.Valor)
                .ThenBy(p => p.Nome, StringComparer.Ordinal)
                .ToList();
        }

        // =====================================================================
        // DETALHE COMPLETO DO DIA (AJAX)
        // =====================================================================

        /// <summary>
        /// Retorna o HTML parcial com TODAS as informacoes do dia selecionado.
        /// Carregado via AJAX ao clicar na barra do grafico - nao recarrega a pagina.
        ///
        /// Inclui:
        /// - Vendas por forma de pagamento (PIX, Dinheiro, etc.)
        /// - Suprimentos com horarios
        /// - Sangrias com horarios
        /// - Status de fechamento do caixa (se houve e quando)
        /// - Itens vendidos (produtos com quantidade e valor)
        /// - Lista de vendas com detalhes
        ///
        /// Leitura pura (regra 25): sem POST/PUT/DELETE, somas em decimal EM C#.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DetalheDia(DateTime? data)
        {
            var hoje = DateTime.Today;

            if (!data.HasValue || data.Value.Date > hoje)
            {
                return PartialView("_DetalheDiaPartial", new DashboardDetalheDiaViewModel
                {
                    Dia = data?.Date ?? hoje,
                    Erro = "Data inválida ou futura."
                });
            }

            var dia = data.Value.Date;
            var fim = dia.AddDays(1);

            // ---- VENDAS DO DIA (com itens para detalhar) ----
            var vendas = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= dia && v.DataVenda < fim)
                .OrderByDescending(v => v.DataVenda)
                .ThenByDescending(v => v.Id)
                .ToListAsync();

            var vendasDetalhe = vendas.Select(v => new DashboardVendaDetalhe
            {
                Id = v.Id,
                DataVenda = v.DataVenda,
                MetodoPagamento = v.MetodoPagamento,
                Total = v.TotalVenda,
                ValorRecebido = v.ValorRecebido,
                ValorTroco = v.ValorTroco,
                CPFCliente = v.CPFCliente,
                CodigoVendedor = v.CodigoVendedor,
                IsFechado = v.IsFechado,
            }).ToList();

            // ---- TOTAIS POR FORMA DE PAGAMENTO ----
            var totalPorMetodo = vendasDetalhe
                .GroupBy(v => v.MetodoPagamento, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DashboardTotalPorMetodo
                {
                    Metodo = g.First().MetodoPagamento,
                    Total = g.Sum(v => v.Total)
                })
                .OrderByDescending(m => m.Total)
                .ToList();

            // ---- MOVIMENTACOES DO DIA (Suprimentos e Sangrias) ----
            var movimentacoes = await _context.CaixaMovimentacoes
                .AsNoTracking()
                .Where(m => m.Data >= dia && m.Data < fim)
                .OrderBy(m => m.Data)
                .ToListAsync();

            var suprimentos = movimentacoes
                .Where(m => m.Tipo.Equals("Suprimento", StringComparison.OrdinalIgnoreCase))
                .Select(m => new DashboardMovimentacaoDetalhe
                {
                    Id = m.Id,
                    Data = m.Data,
                    Tipo = m.Tipo,
                    Valor = m.Valor,
                    Descricao = m.Descricao,
                    IsFechado = m.IsFechado
                })
                .ToList();

            var sangrias = movimentacoes
                .Where(m => m.Tipo.Equals("Sangria", StringComparison.OrdinalIgnoreCase))
                .Select(m => new DashboardMovimentacaoDetalhe
                {
                    Id = m.Id,
                    Data = m.Data,
                    Tipo = m.Tipo,
                    Valor = m.Valor,
                    Descricao = m.Descricao,
                    IsFechado = m.IsFechado
                })
                .ToList();

            // ---- STATUS DE FECHAMENTO DO CAIXA ----
            // Verifica se todas as vendas e movimentacoes do dia estao fechadas
            var vendasFechadas = vendas.All(v => v.IsFechado);
            var movsFechadas = movimentacoes.All(m => m.IsFechado);
            var caixaFechado = vendas.Count > 0 || movimentacoes.Count > 0 ? (vendasFechadas && movsFechadas) : false;

            // Tenta inferir o horario do fechamento pela ultima alteracao de IsFechado
            // (nao temos timestamp separado, entao usamos a data da ultima venda/mov fechada)
            DateTime? horarioFechamento = null;
            if (caixaFechado)
            {
                var ultimasFechadas = new List<DateTime>();
                if (vendas.Any(v => v.IsFechado))
                    ultimasFechadas.Add(vendas.Where(v => v.IsFechado).Max(v => v.DataVenda));
                if (movimentacoes.Any(m => m.IsFechado))
                    ultimasFechadas.Add(movimentacoes.Where(m => m.IsFechado).Max(m => m.Data));

                if (ultimasFechadas.Count > 0)
                    horarioFechamento = ultimasFechadas.Max();
            }

            // ---- PRODUTOS VENDIDOS (itens agrupados) ----
            var produtos = await MontarProdutosDoDiaAsync(dia, fim);

            // ---- ITENS DETALHADOS DE CADA VENDA (para expandir) ----
            var vendasComItens = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= dia && v.DataVenda < fim)
                .Select(v => new
                {
                    v.Id,
                    v.DataVenda,
                    v.MetodoPagamento,
                    v.TotalVenda,
                    Itens = v.Itens.Select(i => new
                    {
                        i.ProdutoId,
                        i.Produto.Nome,
                        i.Produto.Tipo,
                        i.Quantidade,
                        i.PrecoUnitario
                    }).ToList()
                })
                .ToListAsync();

            var vendasComItensDetalhe = vendasComItens.Select(v => new DashboardVendaComItens
            {
                Id = v.Id,
                DataVenda = v.DataVenda,
                MetodoPagamento = v.MetodoPagamento,
                Total = v.TotalVenda,
                Itens = v.Itens.Select(i => new DashboardItemVendaDetalhe
                {
                    ProdutoId = i.ProdutoId,
                    Nome = i.Nome,
                    Quantidade = i.Quantidade,
                    PrecoUnitario = i.PrecoUnitario,
                    PorPeso = i.Tipo == TipoProduto.PorPeso
                }).ToList()
            }).ToList();

return PartialView("_DetalheDiaPartial", new DashboardDetalheDiaViewModel
            {
                Dia = dia,
                TotalDia = vendasDetalhe.Sum(v => v.Total),
                NumeroVendas = vendasDetalhe.Count,
                TicketMedio = vendasDetalhe.Count == 0 ? 0m : Math.Round(vendasDetalhe.Sum(v => v.Total) / vendasDetalhe.Count, 2, MidpointRounding.AwayFromZero),
                Vendas = vendasDetalhe,
                TotalPorMetodo = totalPorMetodo,
                Suprimentos = suprimentos,
                Sangrias = sangrias,
                CaixaFechado = caixaFechado,
                HorarioFechamento = horarioFechamento,
                Produtos = produtos,
                VendasComItens = vendasComItensDetalhe,
                TotalSuprimentos = suprimentos.Sum(s => s.Valor),
                TotalSangrias = Math.Abs(sangrias.Sum(s => s.Valor))
});
        }
    }

// =====================================================================
// TIPOS DO VIEW-MODEL
// =====================================================================

/// <summary>
/// Projecao minima de uma venda dentro da janela: so a data e o total
/// gravado. Carregar a venda inteira arrastaria os itens, que nao servem
/// para nada no grafico.
/// </summary>
public class VendaPeriodo
{
    public DateTime Data { get; set; }
    public decimal Total { get; set; }
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

    /// <summary>O que a tela inteira precisa mostrar, num objeto so.</summary>
    public class DashboardViewModel
    {
        /// <summary>Soma de TotalVenda GRAVADO das vendas de hoje (qualquer metodo, fechadas inclusive).</summary>
        public decimal TotalVendasHoje { get; set; }

        /// <summary>Quantas vendas ocorreram hoje (para o ticket medio).</summary>
        public int NumeroVendasHoje { get; set; }

        /// <summary>Total / numero, arredondado em 2 casas, AwayFromZero.</summary>
        public decimal TicketMedio { get; set; }

        /// <summary>Soma da JANELA INTEIRA (7 ou 30 dias), em TotalVenda gravado.</summary>
        public decimal TotalVendasPeriodo { get; set; }

        /// <summary>Numero de vendas da janela inteira.</summary>
        public int NumeroVendasPeriodo { get; set; }

        /// <summary>Janela escolhida: 7 ou 30. Qualquer entrada fora disso vira 7.</summary>
        public int Dias { get; set; } = 7;

        /// <summary>Soma por metodo, com as chaves normalizadas (caixa alta).</summary>
        public List<DashboardTotalPorMetodo> TotalPorMetodo { get; set; } = new();

        /// <summary>
        /// Saldo fisico da gaveta: vendas em Dinheiro NAO fechadas de hoje +
        /// suprimentos + sangrias. Sangria ja e negativa no banco e por isso
        /// SOMA (fonte unica: CaixaController.CalcularSaldoEmGaveta).
        /// </summary>
        public decimal SaldoGaveta { get; set; }

        /// <summary>A janela em ordem cronologica (7 ou 30 pontos).</summary>
        public List<DashboardDiaSerie> SerieUltimos7Dias { get; set; } = new();

        /// <summary>Produtos abaixo do minimo, os mais criticos primeiro (no maximo 5).</summary>
        public List<DashboardEstoqueBaixo> EstoqueBaixo { get; set; } = new();

        /// <summary>Quantos produtos estao abaixo do minimo NO TOTAL (a lista mostra so 5).</summary>
        public int TotalEstoqueBaixo { get; set; }

        /// <summary>As 5 vendas mais recentes, de qualquer dia.</summary>
        public List<DashboardVendaRecente> UltimasVendas { get; set; } = new();

        /// <summary>
        /// Dia escolhido clicando numa barra do grafico (?data= no proprio
        /// Index). Nulo = nenhuma secao de vendas do dia no painel; data
        /// futura tambem chega nula - nao ha venda no futuro.
        /// </summary>
        public DateTime? DiaSelecionado { get; set; }

        /// <summary>Vendas do dia escolhido, mais recente primeiro.</summary>
        public List<DashboardVendaRecente> VendasDoDia { get; set; } = new();

        /// <summary>Soma do TotalVenda GRAVADO do dia escolhido.</summary>
        public decimal TotalDiaSelecionado { get; set; }

        /// <summary>Quantas vendas o dia escolhido teve (0 = dia sem venda).</summary>
        public int NumeroVendasDia { get; set; }
    }

    /// <summary>Um dia da serie (7 ou 30 dias) da janela do painel.</summary>
    public class DashboardDiaSerie
    {
        public DateTime Dia { get; set; }
        public decimal Total { get; set; }

        /// <summary>
        /// Altura da barra em PORCENTAGEM, calculada no view-model (regra do
        /// grafico): o maior dia da janela = 100, um dia com venda nunca fica
        /// abaixo de 4% (senao arredonda para 0px e some do grafico) e zero
        /// continua 0. Com todos os dias zerados, o divisor e 1 para nao
        /// dividir por zero.
        /// </summary>
        public decimal PercentualAltura { get; set; }
    }

    /// <summary>Detalhe de UM dia: total, ticket medio, produtos e vendas.</summary>
    public class DashboardDiaViewModel
    {
        public DateTime Dia { get; set; }

        /// <summary>Soma do TotalVenda GRAVADO do dia (nunca recalculada).</summary>
        public decimal Total { get; set; }

        public int NumeroVendas { get; set; }
        public decimal TicketMedio { get; set; }

        /// <summary>Janela que veio do painel (7 ou 30), para o link de voltar.</summary>
        public int Dias { get; set; } = 7;

        public List<DashboardDiaProduto> Produtos { get; set; } = new();
        public List<DashboardVendaRecente> Vendas { get; set; } = new();
    }

    /// <summary>Um produto vendido no dia, com quantidade somada EM C#.</summary>
    public class DashboardDiaProduto
    {
        public int ProdutoId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public decimal Valor { get; set; }

        /// <summary>Derivacao existente (regras 19 e 20): kg para PorPeso, un no resto.</summary>
        public bool PorPeso { get; set; }
        public string Unidade => PorPeso ? "kg" : "un";
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

    // =====================================================================
    // VIEW-MODELS DO DETALHE COMPLETO DO DIA (AJAX)
    // =====================================================================

    /// <summary>Detalhe completo de um dia para o partial AJAX.</summary>
    public class DashboardDetalheDiaViewModel
    {
        public DateTime Dia { get; set; }
        public string? Erro { get; set; }

        // Resumo financeiro
        public decimal TotalDia { get; set; }
        public int NumeroVendas { get; set; }
        public decimal TicketMedio { get; set; }
        public decimal TotalSuprimentos { get; set; }
        public decimal TotalSangrias { get; set; }

        // Vendas por forma de pagamento
        public List<DashboardTotalPorMetodo> TotalPorMetodo { get; set; } = new();

        // Movimentacoes
        public List<DashboardMovimentacaoDetalhe> Suprimentos { get; set; } = new();
        public List<DashboardMovimentacaoDetalhe> Sangrias { get; set; } = new();

        // Fechamento do caixa
        public bool CaixaFechado { get; set; }
        public DateTime? HorarioFechamento { get; set; }

        // Produtos vendidos (agrupados)
        public List<DashboardDiaProduto> Produtos { get; set; } = new();

        // Vendas com itens (para expandir)
        public List<DashboardVendaComItens> VendasComItens { get; set; } = new();

        // Vendas simples (lista)
        public List<DashboardVendaDetalhe> Vendas { get; set; } = new();
    }

    /// <summary>Uma venda com detalhes basicos para a lista.</summary>
    public class DashboardVendaDetalhe
    {
        public int Id { get; set; }
        public DateTime DataVenda { get; set; }
        public string MetodoPagamento { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal ValorRecebido { get; set; }
        public decimal ValorTroco { get; set; }
        public string? CPFCliente { get; set; }
        public string? CodigoVendedor { get; set; }
        public bool IsFechado { get; set; }
    }

    /// <summary>Uma movimentacao (suprimento/sangria) com detalhes.</summary>
    public class DashboardMovimentacaoDetalhe
    {
        public int Id { get; set; }
        public DateTime Data { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public bool IsFechado { get; set; }
    }

    /// <summary>Item de venda para o detalhe expandido.</summary>
    public class DashboardItemVendaDetalhe
    {
        public int ProdutoId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public bool PorPeso { get; set; }
        public string Unidade => PorPeso ? "kg" : "un";
        public decimal Subtotal => Math.Round(PrecoUnitario * Quantidade, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Venda com seus itens para expandir na tela.</summary>
    public class DashboardVendaComItens
    {
        public int Id { get; set; }
        public DateTime DataVenda { get; set; }
        public string MetodoPagamento { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public List<DashboardItemVendaDetalhe> Itens { get; set; } = new();
    }
}