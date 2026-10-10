using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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
        public async Task<IActionResult> Index(int? dias = null, DateTime? data = null, string? periodo = null)
        {
            var inicioHoje = DateTime.Today;
            var fimHoje = inicioHoje.AddDays(1);

            // ---- Determinar período (periodo prevalece sobre dias) ----
            var (periodoInicio, periodoFim, periodoAnteriorInicio, periodoAnteriorFim, periodoNome, bucketCount, bucketTipo) = ResolverPeriodo(periodo, dias, inicioHoje);

            // ---- Vendas do período ATUAL ----
            var vendasPeriodo = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= periodoInicio && v.DataVenda < periodoFim)
                .Select(v => new VendaPeriodo { Data = v.DataVenda, Total = v.TotalVenda })
                .ToListAsync();

            var faturamentoAtual = vendasPeriodo.Sum(v => v.Total);
            var vendasAtual = vendasPeriodo.Count;
            var ticketMedioAtual = vendasAtual == 0 ? 0m : Math.Round(faturamentoAtual / vendasAtual, 2, MidpointRounding.AwayFromZero);

            // ---- Vendas do período ANTERIOR ----
            var vendasAnterior = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= periodoAnteriorInicio && v.DataVenda < periodoAnteriorFim)
                .Select(v => new VendaPeriodo { Data = v.DataVenda, Total = v.TotalVenda })
                .ToListAsync();

            var faturamentoAnterior = vendasAnterior.Sum(v => v.Total);
            var vendasAnteriorCount = vendasAnterior.Count;
            var ticketMedioAnterior = vendasAnteriorCount == 0 ? 0m : Math.Round(faturamentoAnterior / vendasAnteriorCount, 2, MidpointRounding.AwayFromZero);

            // ---- Variação % ----
            decimal? variacaoPercentual = null;
            string estadoVariacao = "SemBase";
            if (faturamentoAnterior > 0m)
            {
                variacaoPercentual = Math.Round((faturamentoAtual - faturamentoAnterior) / faturamentoAnterior * 100m, 1, MidpointRounding.AwayFromZero);
                if (variacaoPercentual > 0m) estadoVariacao = "Subiu";
                else if (variacaoPercentual < 0m) estadoVariacao = "Caiu";
                else estadoVariacao = "Estavel";
            }

            // ---- Frase do cabeçalho ----
            string fraseCabecalho;
            if (faturamentoAtual == 0m && faturamentoAnterior == 0m)
                fraseCabecalho = "Sem vendas no período.";
            else if (faturamentoAnterior == 0m)
                fraseCabecalho = "Ainda não há vendas no período anterior para comparar.";
            else if (estadoVariacao == "Subiu")
                fraseCabecalho = $"As vendas subiram {variacaoPercentual.Value.ToString("F1", CultureInfo.InvariantCulture)}% em relação ao período anterior.";
            else if (estadoVariacao == "Caiu")
                fraseCabecalho = $"As vendas caíram {Math.Abs(variacaoPercentual.Value).ToString("F1", CultureInfo.InvariantCulture)}% em relação ao período anterior.";
            else
                fraseCabecalho = "As vendas ficaram estáveis em relação ao período anterior.";

            // ---- Saudação ----
            var hora = DateTime.Now.Hour;
            string saudacaoPrefixo = hora < 12 ? "Bom dia" : (hora < 18 ? "Boa tarde" : "Boa noite");
            string nomeUsuario = ObterNomeExibidoDoUsuario();
            string saudacao = $"{saudacaoPrefixo}, {nomeUsuario}";

            // ---- Gaveta (regra 7) - SEMPRE baseada em HOJE, não muda com período ----
            var movimentosHoje = await _context.CaixaMovimentacoes
                .AsNoTracking()
                .Where(m => m.Data >= inicioHoje && m.Data < fimHoje && !m.IsFechado)
                .ToListAsync();

            var vendasHojeGaveta = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= inicioHoje && v.DataVenda < fimHoje
                            && !v.IsFechado
                            && v.MetodoPagamento == CaixaController.MetodoPagamentoDinheiro)
                .ToListAsync();

            var vendasDinheiro = vendasHojeGaveta.Sum(v => v.TotalVenda);
            var suprimentos = movimentosHoje.Where(m => m.Tipo == "Suprimento").Sum(m => m.Valor);
            var sangrias = movimentosHoje.Where(m => m.Tipo == "Sangria").Sum(m => m.Valor);
            var saldoGaveta = CaixaController.CalcularSaldoEmGaveta(vendasDinheiro, suprimentos, sangrias);

            // ---- Estoque baixo ----
            var (estoqueBaixo, totalEstoqueBaixo) = await MontarEstoqueBaixoAsync();

            // ---- Últimas vendas ----
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

            // ---- Série ATUAL (buckets) ----
            var serieAtual = MontarSerieBuckets(vendasPeriodo, periodoInicio, periodoFim, bucketCount, bucketTipo);

            // ---- Série ANTERIOR (buckets) ----
            var serieAnterior = MontarSerieBuckets(vendasAnterior, periodoAnteriorInicio, periodoAnteriorFim, bucketCount, bucketTipo);
            PreencherSerieAnterior(serieAtual, vendasAnterior, periodoAnteriorInicio, bucketCount, bucketTipo);

            // ---- Top 5 mais vendidos (por valor) ----
            var maisVendidos = await MontarMaisVendidosAsync(periodoInicio, periodoFim);

            // ---- Formas de pagamento (período atual) ----
            var formasPagamento = MontarFormasPagamento(vendasPeriodo);

            // ---- Vendas de hoje (para TotalVendasHoje, NumeroVendasHoje, TicketMedio) ----
            var vendasHoje = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= inicioHoje && v.DataVenda < fimHoje)
                .ToListAsync();

            var totalHoje = vendasHoje.Sum(v => v.TotalVenda);
            var numeroVendasHoje = vendasHoje.Count;
            var ticketMedioHoje = numeroVendasHoje == 0 ? 0m : Math.Round(totalHoje / numeroVendasHoje, 2, MidpointRounding.AwayFromZero);
            var porMetodoHoje = MontarPorMetodo(vendasHoje);

            // ---- Total da janela legada (7 ou 30 dias) para compatibilidade ----
            var janelaLegada = NormalizarDias(dias);
            var inicioJanelaLegada = inicioHoje.AddDays(-(janelaLegada - 1));
            var vendasJanelaLegada = await _context.Vendas
                .AsNoTracking()
                .Where(v => v.DataVenda >= inicioJanelaLegada && v.DataVenda < fimHoje)
                .Select(v => new VendaPeriodo { Data = v.DataVenda, Total = v.TotalVenda })
                .ToListAsync();

            var totalPeriodoLegado = vendasJanelaLegada.Sum(v => v.Total);
            var numeroPeriodoLegado = vendasJanelaLegada.Count;
            var serieLegada = MontarSerie(vendasJanelaLegada, inicioHoje, janelaLegada);

            // ---- Vendas do dia selecionado (?data=) ----
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
                // Legados
                TotalVendasHoje = totalHoje,
                NumeroVendasHoje = numeroVendasHoje,
                TicketMedio = ticketMedioHoje,
                TotalPorMetodo = porMetodoHoje,
                SaldoGaveta = saldoGaveta,
                SerieUltimos7Dias = serieLegada,
                EstoqueBaixo = estoqueBaixo,
                TotalEstoqueBaixo = totalEstoqueBaixo,
                UltimasVendas = ultimasVendas,
                TotalVendasPeriodo = totalPeriodoLegado,
                NumeroVendasPeriodo = numeroPeriodoLegado,
                DiaSelecionado = diaSelecionado,
                VendasDoDia = vendasDoDia,
                TotalDiaSelecionado = totalDiaSelecionado,
                NumeroVendasDia = vendasDoDia.Count,
                Dias = janelaLegada,

                // Novos (Ajuste 4)
                Periodo = periodoNome,
                PeriodoInicio = periodoInicio,
                PeriodoFim = periodoFim,
                PeriodoAnteriorInicio = periodoAnteriorInicio,
                PeriodoAnteriorFim = periodoAnteriorFim,
                FaturamentoAtual = faturamentoAtual,
                VendasAtual = vendasAtual,
                TicketMedioAtual = ticketMedioAtual,
                FaturamentoAnterior = faturamentoAnterior,
                VendasAnterior = vendasAnteriorCount,
                TicketMedioAnterior = ticketMedioAnterior,
                VariacaoPercentual = variacaoPercentual,
                EstadoVariacao = estadoVariacao,
                FraseCabecalho = fraseCabecalho,
                Saudacao = saudacao,
                SerieAtual = serieAtual,
                SerieAnterior = serieAnterior,
                MaisVendidos = maisVendidos,
                FormasPagamento = formasPagamento,
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

        // =====================================================================
        // HELPERS AJUSTE 4 - PERÍODOS, SÉRIES, TOP PRODUTOS, FORMAS PAGAMENTO
        // =====================================================================

        /// <summary>
        /// Resolve o período a partir do parâmetro string (hoje, ontem, 7dias, 30dias, mes, ano).
        /// Retorna: (inicioAtual, fimAtual, inicioAnterior, fimAnterior, nomePeriodo, bucketCount, bucketTipo).
        /// bucketTipo: "hora", "dia", "mes".
        /// Inválido ou ausente = hoje; só dias (7 ou 30) = 7dias ou 30dias; periodo prevalece.
        /// </summary>
        private static (DateTime inicioAtual, DateTime fimAtual, DateTime inicioAnterior, DateTime fimAnterior, string nomePeriodo, int bucketCount, string bucketTipo) ResolverPeriodo(string? periodo, int? dias, DateTime hoje)
        {
            var H = hoje.Date; // hoje às 00:00
            var fimAtualBase = H.AddDays(1); // exclusivo

            string p = (periodo ?? "").Trim().ToLowerInvariant();

            // Se periodo não informado ou inválido, verifica dias (legado)
            if (string.IsNullOrEmpty(p))
            {
                var janela = NormalizarDias(dias);
                if (janela == 30) p = "30dias";
                else p = "7dias"; // 7 ou qualquer outro vira 7
            }
            else if (p == "7" || p == "30")
            {
                // dias legado sem "dias"
                p = p + "dias";
            }

            DateTime inicioAtual, fimAtual, inicioAnterior, fimAnterior;
            string nomePeriodo;
            int bucketCount;
            string bucketTipo;

            switch (p)
            {
                case "hoje":
                    inicioAtual = H;
                    fimAtual = fimAtualBase;
                    inicioAnterior = H.AddDays(-1);
                    fimAnterior = H;
                    nomePeriodo = "hoje";
                    bucketCount = 24;
                    bucketTipo = "hora";
                    break;

                case "ontem":
                    inicioAtual = H.AddDays(-1);
                    fimAtual = H;
                    inicioAnterior = H.AddDays(-2);
                    fimAnterior = H.AddDays(-1);
                    nomePeriodo = "ontem";
                    bucketCount = 24;
                    bucketTipo = "hora";
                    break;

                case "7dias":
                    inicioAtual = H.AddDays(-6);
                    fimAtual = fimAtualBase;
                    inicioAnterior = H.AddDays(-13);
                    fimAnterior = H.AddDays(-6);
                    nomePeriodo = "7dias";
                    bucketCount = 7;
                    bucketTipo = "dia";
                    break;

                case "30dias":
                    inicioAtual = H.AddDays(-29);
                    fimAtual = fimAtualBase;
                    inicioAnterior = H.AddDays(-59);
                    fimAnterior = H.AddDays(-29);
                    nomePeriodo = "30dias";
                    bucketCount = 30;
                    bucketTipo = "dia";
                    break;

                case "mes":
                    var primeiroMesAtual = new DateTime(H.Year, H.Month, 1);
                    inicioAtual = primeiroMesAtual;
                    fimAtual = fimAtualBase;
                    var primeiroMesAnterior = primeiroMesAtual.AddMonths(-1);
                    var ultimoDiaMesAnterior = primeiroMesAtual.AddDays(-1).Day;
                    var N = Math.Min(H.Day, ultimoDiaMesAnterior);
                    inicioAnterior = primeiroMesAnterior;
                    fimAnterior = new DateTime(primeiroMesAnterior.Year, primeiroMesAnterior.Month, N).AddDays(1);
                    nomePeriodo = "mes";
                    bucketCount = H.Day; // dias do mês até hoje
                    bucketTipo = "dia";
                    break;

                case "ano":
                    var primeiroAnoAtual = new DateTime(H.Year, 1, 1);
                    inicioAtual = primeiroAnoAtual;
                    fimAtual = fimAtualBase;
                    var primeiroAnoAnterior = new DateTime(H.Year - 1, 1, 1);
                    // 29/02 vira 28/02 no ano anterior se ano bissexto
                    int diaAno = H.DayOfYear;
                    DateTime fimAnteriorAno;
                    try
                    {
                        fimAnteriorAno = new DateTime(H.Year - 1, H.Month, H.Day).AddDays(1);
                    }
                    catch
                    {
                        // 29/02 em ano não bissexto
                        fimAnteriorAno = new DateTime(H.Year - 1, 2, 28).AddDays(1);
                    }
                    inicioAnterior = primeiroAnoAnterior;
                    fimAnterior = fimAnteriorAno;
                    nomePeriodo = "ano";
                    bucketCount = H.Month; // meses até o atual
                    bucketTipo = "mes";
                    break;

                default:
                    // Inválido -> hoje
                    goto case "hoje";
            }

            return (inicioAtual, fimAtual, inicioAnterior, fimAnterior, nomePeriodo, bucketCount, bucketTipo);
        }

        /// <summary>
        /// Monta série de buckets (horas, dias ou meses) para o gráfico.
        /// Retorna lista com ValorAtual e ValorAnterior (null = sem dado no anterior).
        /// Rótulos: horas "00h".."23h", dias "dd/MM", meses "jan".."dez".
        /// Valor futuro (horas depois de agora em "hoje") = null.
        /// Valor inexistente na série anterior (mês anterior menor) = null.
        /// Soma dos buckets atuais = faturamento do período.
        /// </summary>
        private static List<DashboardBucketSerie> MontarSerieBuckets(
            List<VendaPeriodo> vendas, DateTime inicio, DateTime fim, int bucketCount, string bucketTipo)
        {
            var resultado = new List<DashboardBucketSerie>(bucketCount);
            var agora = DateTime.Now;

            // Agrupar vendas por bucket
            Dictionary<int, decimal> somaPorBucket = new();

            foreach (var v in vendas)
            {
                int indice;
                if (bucketTipo == "hora")
                {
                    indice = v.Data.Hour;
                }
                else if (bucketTipo == "dia")
                {
                    indice = (v.Data.Date - inicio.Date).Days;
                }
                else // mes
                {
                    indice = v.Data.Month - 1; // 0-based
                }

                if (indice >= 0 && indice < bucketCount)
                {
                    if (somaPorBucket.TryGetValue(indice, out var existente))
                        somaPorBucket[indice] = existente + v.Total;
                    else
                        somaPorBucket[indice] = v.Total;
                }
            }

            for (int i = 0; i < bucketCount; i++)
            {
                string rotulo;
                if (bucketTipo == "hora")
                    rotulo = $"{i:00}h";
                else if (bucketTipo == "dia")
                    rotulo = inicio.AddDays(i).ToString("dd/MM");
                else // mes
                    rotulo = new DateTime(2000, i + 1, 1).ToString("MMM", CultureInfo.InvariantCulture).ToLowerInvariant(); // "jan", "fev"...

                decimal? valorAtual = null;
                if (somaPorBucket.TryGetValue(i, out var valor))
                    valorAtual = valor;

                // Futuro: horas depois de agora em "hoje"
                if (bucketTipo == "hora" && i > agora.Hour && inicio.Date == DateTime.Today)
                    valorAtual = null;

                resultado.Add(new DashboardBucketSerie
                {
                    Indice = i,
                    Rotulo = rotulo,
                    ValorAtual = valorAtual,
                    ValorAnterior = null // será preenchido depois
                });
            }

            return resultado;
        }

        /// <summary>
        /// Preenche a série anterior nos buckets já criados.
        /// </summary>
        private static void PreencherSerieAnterior(List<DashboardBucketSerie> serieAtual, List<VendaPeriodo> vendasAnterior, DateTime inicioAnterior, int bucketCount, string bucketTipo)
        {
            Dictionary<int, decimal> somaPorBucket = new();

            foreach (var v in vendasAnterior)
            {
                int indice;
                if (bucketTipo == "hora")
                {
                    indice = v.Data.Hour;
                }
                else if (bucketTipo == "dia")
                {
                    indice = (v.Data.Date - inicioAnterior.Date).Days;
                }
                else // mes
                {
                    indice = v.Data.Month - 1;
                }

                if (indice >= 0 && indice < bucketCount)
                {
                    if (somaPorBucket.TryGetValue(indice, out var existente))
                        somaPorBucket[indice] = existente + v.Total;
                    else
                        somaPorBucket[indice] = v.Total;
                }
            }

            for (int i = 0; i < serieAtual.Count; i++)
            {
                if (somaPorBucket.TryGetValue(i, out var valor))
                    serieAtual[i].ValorAnterior = valor;
                else
                    serieAtual[i].ValorAnterior = null;
            }
        }

        /// <summary>
        /// Monta Top 5 produtos mais vendidos por VALOR no período.
        /// Quantidade somada EM C# (kg/un), arredondamento só no fim.
        /// Inclui inativos e vendas fechadas.
        /// Desempate por nome.
        /// </summary>
        private async Task<List<DashboardTopProduto>> MontarMaisVendidosAsync(DateTime inicio, DateTime fim)
        {
            var itens = await _context.VendaItens
                .AsNoTracking()
                .Include(i => i.Produto)
                .Where(i => i.Venda.DataVenda >= inicio && i.Venda.DataVenda < fim)
                .Select(i => new
                {
                    i.ProdutoId,
                    Nome = i.Produto.Nome,
                    Tipo = i.Produto.Tipo,
                    i.Quantidade,
                    i.PrecoUnitario,
                })
                .ToListAsync();

            var agrupados = itens
                .GroupBy(i => i.ProdutoId)
                .Select(g =>
                {
                    var quantidade = g.Sum(i => i.Quantidade);
                    var valor = Math.Round(g.Sum(i => i.PrecoUnitario * i.Quantidade), 2, MidpointRounding.AwayFromZero);
                    var tipo = g.First().Tipo;
                    return new
                    {
                        ProdutoId = g.Key,
                        Nome = g.First().Nome,
                        Quantidade = quantidade,
                        Valor = valor,
                        Unidade = tipo == TipoProduto.PorPeso ? "kg" : "un",
                    };
                })
                .OrderByDescending(x => x.Valor)
                .ThenBy(x => x.Nome, StringComparer.Ordinal)
                .Take(5)
                .ToList();

            var maior = agrupados.Count > 0 ? agrupados.Max(x => x.Valor) : 0m;

            return agrupados.Select(x => new DashboardTopProduto
            {
                ProdutoId = x.ProdutoId,
                Nome = x.Nome,
                Quantidade = x.Quantidade,
                Unidade = x.Unidade,
                Valor = x.Valor,
                BarraRelativa = maior > 0m ? Math.Round(x.Valor / maior * 100m, 1, MidpointRounding.AwayFromZero) : 0m,
            }).ToList();
        }

        /// <summary>
        /// Monta formas de pagamento do período atual para a rosca.
        /// Agrupa sem diferenciar maiúsculas; valor, % (1 casa; último absorve arredondamento);
        /// cor por forma (Dinheiro green, Pix cyan, Crédito violet, Débito blue, Voucher amber, demais slate).
        /// Segmentos: círculo r=40, circunferência 2*PI*40; dasharray e offset calculados.
        /// Ordenado por valor desc.
        /// </summary>
        private static List<DashboardFormaPagamento> MontarFormasPagamento(List<VendaPeriodo> vendasPeriodo)
        {
            // Agrupar por método (case-insensitive) mantendo o primeiro nome original
            var grupos = vendasPeriodo
                .GroupBy(v => v.MetodoPagamento, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Metodo = g.First().MetodoPagamento,
                    Total = g.Sum(v => v.Total)
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            var totalGeral = grupos.Sum(x => x.Total);
            if (totalGeral == 0m) return new List<DashboardFormaPagamento>();

            var cores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Dinheiro", "green" },
                { "Pix", "cyan" },
                { "Credito", "violet" },
                { "Debito", "blue" },
                { "Voucher", "amber" },
            };

            const decimal raio = 40m;
            const decimal circunferencia = 2m * (decimal)Math.PI * raio;

            var resultado = new List<DashboardFormaPagamento>();
            decimal acumuladoPercentual = 0m;

            for (int i = 0; i < grupos.Count; i++)
            {
                var g = grupos[i];
                var percentual = totalGeral > 0m ? g.Total / totalGeral * 100m : 0m;

                // Último segmento absorve o arredondamento para somar 100,0
                if (i == grupos.Count - 1)
                    percentual = 100m - acumuladoPercentual;
                else
                    percentual = Math.Round(percentual, 1, MidpointRounding.AwayFromZero);

                acumuladoPercentual += percentual;

                var cor = cores.TryGetValue(g.Metodo, out var c) ? c : "slate";

                var dashArray = circunferencia * percentual / 100m;
                var dashOffset = circunferencia * (100m - acumuladoPercentual) / 100m;

                resultado.Add(new DashboardFormaPagamento
                {
                    Metodo = g.Metodo,
                    Valor = g.Total,
                    Percentual = percentual,
                    Cor = cor,
                    DashArray = Math.Round(dashArray, 2, MidpointRounding.AwayFromZero),
                    DashOffset = Math.Round(dashOffset, 2, MidpointRounding.AwayFromZero),
                });
            }

            return resultado;
        }

        /// <summary>
        /// Obtém o nome exibido do usuário logado (mesmo do botão da sidebar).
        /// Nunca o e-mail, nunca contém "@". Fallback "Dono".
        /// </summary>
        private string ObterNomeExibidoDoUsuario()
        {
            // O _Layout usa User.Identity.Name para o e-mail e busca o NomeExibicao no banco
            // Aqui fazemos o mesmo: pega o email do Identity e busca o nome
            var email = User?.Identity?.Name;
            if (!string.IsNullOrEmpty(email))
            {
                try
                {
                    var usuario = _context?.Users.AsNoTracking().FirstOrDefault(u => u.Email == email);
                    if (usuario != null && !string.IsNullOrEmpty(usuario.NomeExibicao))
                        return usuario.NomeExibicao;
                }
                catch
                {
                    // Se falhar (ex.: banco não inicializado em testes), usa fallback
                }
            }
            return "Dono";
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
    public string MetodoPagamento { get; set; } = string.Empty;
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

        // ===== NOVOS CAMPOS AJUSTE 4 =====
        /// <summary>Período selecionado: hoje, ontem, 7dias, 30dias, mes, ano.</summary>
        public string Periodo { get; set; } = "hoje";

        /// <summary>Início do período atual (inclusivo).</summary>
        public DateTime PeriodoInicio { get; set; }

        /// <summary>Fim do período atual (exclusivo).</summary>
        public DateTime PeriodoFim { get; set; }

        /// <summary>Início do período anterior (inclusivo).</summary>
        public DateTime PeriodoAnteriorInicio { get; set; }

        /// <summary>Fim do período anterior (exclusivo).</summary>
        public DateTime PeriodoAnteriorFim { get; set; }

        /// <summary>Faturamento do período atual.</summary>
        public decimal FaturamentoAtual { get; set; }

        /// <summary>Vendas (contagem) do período atual.</summary>
        public int VendasAtual { get; set; }

        /// <summary>Ticket médio do período atual.</summary>
        public decimal TicketMedioAtual { get; set; }

        /// <summary>Faturamento do período anterior.</summary>
        public decimal FaturamentoAnterior { get; set; }

        /// <summary>Vendas (contagem) do período anterior.</summary>
        public int VendasAnterior { get; set; }

        /// <summary>Ticket médio do período anterior.</summary>
        public decimal TicketMedioAnterior { get; set; }

        /// <summary>Variação percentual do faturamento (1 casa, AwayFromZero). Nulo se anterior = 0.</summary>
        public decimal? VariacaoPercentual { get; set; }

        /// <summary>Estado da variação: Subiu, Caiu, Estavel, SemBase.</summary>
        public string EstadoVariacao { get; set; } = "SemBase";

        /// <summary>Frase do cabeçalho comparando com período anterior.</summary>
        public string FraseCabecalho { get; set; } = string.Empty;

        /// <summary>Saudação: Bom dia / Boa tarde / Boa noite + nome do usuário.</summary>
        public string Saudacao { get; set; } = string.Empty;

        /// <summary>Série do gráfico do período ATUAL (faturamento por bucket).</summary>
        public List<DashboardBucketSerie> SerieAtual { get; set; } = new();

        /// <summary>Série do gráfico do período ANTERIOR (faturamento por bucket). Nulos = sem dado.</summary>
        public List<DashboardBucketSerie> SerieAnterior { get; set; } = new();

        /// <summary>Top 5 produtos mais vendidos por valor no período atual.</summary>
        public List<DashboardTopProduto> MaisVendidos { get; set; } = new();

        /// <summary>Formas de pagamento do período atual (valor, %, cor, dasharray/offset).</summary>
        public List<DashboardFormaPagamento> FormasPagamento { get; set; } = new();
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
        public decimal ValorRecebido { get; set; }
        public decimal ValorTroco { get; set; }
        public string? CodigoVendedor { get; set; }
        public bool IsFechado { get; set; }
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

    /// <summary>Um suprimento para o detalhe do dia.</summary>
    public class DashboardSuprimentoDetalhe
    {
        public DateTime Data { get; set; }
        public decimal Valor { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public bool IsFechado { get; set; }
    }

    /// <summary>Uma sangria para o detalhe do dia.</summary>
    public class DashboardSangriaDetalhe
    {
        public DateTime Data { get; set; }
        public decimal Valor { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public bool IsFechado { get; set; }
    }

    /// <summary>Um produto vendido no detalhe do dia.</summary>
    public class DashboardProdutoDetalhe
    {
        public int ProdutoId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public string Unidade { get; set; } = string.Empty;
        public decimal Valor { get; set; }
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

    /// <summary>Um bucket da série do gráfico (pode ser hora, dia ou mês).</summary>
    public class DashboardBucketSerie
    {
        public int Indice { get; set; }
        public string Rotulo { get; set; } = string.Empty;
        public decimal? ValorAtual { get; set; }
        public decimal? ValorAnterior { get; set; }
    }

    /// <summary>Top produto mais vendido no período.</summary>
    public class DashboardTopProduto
    {
        public int ProdutoId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public string Unidade { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public decimal BarraRelativa { get; set; } // 0..100 relativo ao maior
    }

    /// <summary>Forma de pagamento para a rosca.</summary>
    public class DashboardFormaPagamento
    {
        public string Metodo { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public decimal Percentual { get; set; } // 1 casa, soma 100.0
        public string Cor { get; set; } = string.Empty;
        public decimal DashArray { get; set; } // circunferência * percentual / 100
        public decimal DashOffset { get; set; } // acumulado anterior
    }
}