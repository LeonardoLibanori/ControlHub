using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace ControleEstoqueLoja.Controllers
{
    public class CaixaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CaixaController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // 1. Busca os produtos no banco de dados
            var produtos = await _context.Produtos.ToListAsync();

            // 2. Alimenta a ViewBag que o seu HTML (o @foreach) está procurando
            ViewBag.Produtos = produtos;

            // 3. Mantém o contador caso você use em algum lugar da tela
            ViewBag.TotalProdutos = produtos.Count;

            // 4. Retorna a lista para a View (Model principal)
            return View(produtos);
        }

        // --- MÉTODOS DE FINALIZAÇÃO E ESTOQUE ---

        [HttpPost]
        public async Task<IActionResult> FinalizarVenda(string metodoPagamento, string cpfCliente, string codigoVendedor, decimal valorRecebido, decimal valorTroco, List<ItemVendaSimples> itens)
        {
            // Validação do Vendedor
            var vendedor = await _context.Vendedores
                .FirstOrDefaultAsync(v => v.Codigo == codigoVendedor && v.Ativo);

            if (vendedor == null)
            {
                return Json(new { success = false, message = $"O cupom '{codigoVendedor}' não foi encontrado!" });
            }

            // FASE 1: método de pagamento é obrigatório (evita TEXT NOT NULL / HTTP 500).
            if (string.IsNullOrWhiteSpace(metodoPagamento))
            {
                return Json(new { success = false, message = "Método de pagamento é obrigatório." });
            }

            if (itens == null || !itens.Any()) return BadRequest();

            var novaVenda = new Venda
            {
                DataVenda = DateTime.Now,
                MetodoPagamento = metodoPagamento,
                CPFCliente = cpfCliente,
                CodigoVendedor = codigoVendedor,
                TotalVenda = 0,
                ValorRecebido = 0, // recalculado abaixo a partir do total (nunca confia no POST)
                ValorTroco = 0,
                Itens = new List<VendaItem>()
            };

            foreach (var item in itens)
            {
                // FASE 1: produto inexistente invalida a venda (sem venda vazia / sem "success").
                if (!int.TryParse(item.ProdutoId, out int idProd))
                {
                    return Json(new { success = false, message = "Produto inválido na venda." });
                }

                var produto = await _context.Produtos.FindAsync(idProd);

                if (produto == null)
                {
                    return Json(new { success = false, message = "Produto inválido na venda." });
                }
                    // 1. Conversão de quantidade com validação (TryParse: nunca derruba com 500).
                    if (!decimal.TryParse(item.Quantidade?.Replace(",", "."),
                            NumberStyles.Number, CultureInfo.InvariantCulture, out decimal qtdVendida))
                    {
                        return Json(new { success = false, message = "Quantidade inválida." });
                    }

                    // FASE 1: rejeita quantidade <= 0 (negativa soma estoque; zero gera lixo no histórico).
                    if (qtdVendida <= 0)
                    {
                        return Json(new { success = false, message = $"Quantidade inválida para {produto.Nome}." });
                    }

                    // FASE 1: produto unitário (não granel) só aceita quantidade inteira.
                    string tipoRegra = produto.Tipo.ToString().ToUpper();
                    if (tipoRegra != "GRANEL" && tipoRegra != "PESO" && qtdVendida != decimal.Truncate(qtdVendida))
                    {
                        return Json(new { success = false, message = $"Quantidade fracionada não permitida para {produto.Nome}." });
                    }

                    // 2. VALIDAÇÃO DE ESTOQUE COM MENSAGEM DETALHADA
                    if (produto.Quantidade < qtdVendida)
                    {
                        string unidadeMedida = (tipoRegra == "PESO" || tipoRegra == "GRANEL") ? "kg" : "un";

                        // Retorna o erro parando o loop e avisando o vendedor exatamente o saldo
                        return Json(new
                        {
                            success = false,
                            message = $"Estoque insuficiente para {produto.Nome}. \n" +
                                      $"Tentativa de venda: {qtdVendida}{unidadeMedida}. \n" +
                                      $"Disponível em estoque: {produto.Quantidade}{unidadeMedida}."
                        });
                    }

                    // FASE 1: o preço de venda é SEMPRE o preço de tabela (produto.Preco).
                    // O campo item.Preco do POST é ignorado para impedir fraude via POST manual.
                    // Granel: a tabela guarda o preço por 100 g -> o preço efetivo por kg é preco*10.
                    // Regra centralizada em CalcularSubtotalItem (única fonte de verdade).
                    decimal precoUnit = CalcularSubtotalItem(produto, 1m);
                    decimal subtotalItem = CalcularSubtotalItem(produto, qtdVendida);

                    // 4. BAIXA NO ESTOQUE E ACÚMULO NO TOTAL DA VENDA
                    produto.Quantidade -= qtdVendida;
                    novaVenda.TotalVenda += subtotalItem;

                    // 5. ADICIONA O ITEM À LISTA DA VENDA
                    novaVenda.Itens.Add(new VendaItem
                    {
                        ProdutoId = idProd,
                        Quantidade = qtdVendida,
                        PrecoUnitario = precoUnit
                    });

                    // Marca o produto como alterado para o Entity Framework
                    _context.Produtos.Update(produto);
            }

            // FASE 1: nenhum item válido -> não grava venda vazia.
            if (!novaVenda.Itens.Any())
            {
                return Json(new { success = false, message = "Venda sem itens válidos." });
            }

            // FASE 1: servidor recalcula recebido/troco a partir do total (nunca confia no POST).
            // QA-V04 como especificação: mantém o recebido informado e recalcula troco = recebido - total.
            // QA-V01 como especificação: mesmo com recebido < total a venda é gravada (sem 500 / sem rejeição).
            if (metodoPagamento.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase))
            {
                novaVenda.ValorRecebido = valorRecebido;
                novaVenda.ValorTroco = valorRecebido - novaVenda.TotalVenda;
            }
            else
            {
                novaVenda.ValorRecebido = novaVenda.TotalVenda;
                novaVenda.ValorTroco = 0;
            }

            _context.Vendas.Add(novaVenda);
            await _context.SaveChangesAsync();

            return Json(new { success = true, vendaId = novaVenda.Id });
        }

        // --- MÉTODOS DO MENU ADMINISTRATIVO ---

        [HttpPost]
        public IActionResult RegistrarMovimentacao(string tipo, string valor, string obs)
        {
            try
            {
                // Converte o valor (ex: "10.50")
                decimal valorDecimal = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);

                // BUG-06: valor <= 0 gerava movimentacao fantasma (Sangria 0 / Suprimento 0)
                // e poluia o relatorio, porque Math.Abs transformava o valor em 0 e o registro passava.
                if (valorDecimal <= 0m)
                {
                    return Json(new { success = false, message = "O valor deve ser maior que zero." });
                }

                bool ehSangria = tipo.Contains("Sangria", StringComparison.OrdinalIgnoreCase);

                // BUG-05: sem esta validacao a gaveta pode ficar negativa: o caixa
                // retirava mais dinheiro do que havia. Valida contra o saldo real,
                // considerando SO vendas em Dinheiro (fonte unica CalcularSaldoEmGaveta).
                if (ehSangria)
                {
                    DateTime hoje = DateTime.Today;

                    decimal vendasDinheiro = _context.Vendas
                        .Where(v => v.DataVenda.Date == hoje && !v.IsFechado
                                    && v.MetodoPagamento == MetodoPagamentoDinheiro)
                        .Sum(v => (decimal?)v.TotalVenda) ?? 0m;

                    var suprimentosHoje = _context.CaixaMovimentacoes
                        .Where(m => m.Tipo == "Suprimento" && m.Data.Date == hoje && !m.IsFechado)
                        .ToList()
                        .Sum(m => m.Valor);

                    var sangriasHoje = _context.CaixaMovimentacoes
                        .Where(m => m.Tipo == "Sangria" && m.Data.Date == hoje && !m.IsFechado)
                        .ToList()
                        .Sum(m => m.Valor);

                    decimal saldoAtual = CalcularSaldoEmGaveta(vendasDinheiro, suprimentosHoje, sangriasHoje);

                    if (valorDecimal > saldoAtual)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Sangria maior que o saldo em gaveta. Disponivel: R$ {saldoAtual:N2}."
                        });
                    }
                }

                var movimentacao = new CaixaMovimentacao
                {
                    Data = DateTime.Now,
                    // Forçamos o nome padrão aqui para o relatório nunca falhar
                    Tipo = ehSangria ? "Sangria" : "Suprimento",
                    Valor = ehSangria ? -Math.Abs(valorDecimal) : Math.Abs(valorDecimal),
                    Descricao = string.IsNullOrWhiteSpace(obs) ? tipo : obs
                };

                _context.CaixaMovimentacoes.Add(movimentacao);
                _context.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Pega a mensagem real do erro (InnerException)
                var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return Json(new { success = false, message = "Erro ao salvar: " + msg });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObterStatusAtual()
        {
            var hoje = DateTime.Today;

            // LER DE CaixaMovimentacoes, NÃO DE FluxoCaixa
            var suprimentos = await _context.CaixaMovimentacoes
                .Where(f => f.Tipo == "Suprimento" && f.Data.Date == hoje)
                .SumAsync(f => (decimal?)f.Valor) ?? 0m;

            var sangrias = await _context.CaixaMovimentacoes
                .Where(f => f.Tipo == "Sangria" && f.Data.Date == hoje)
                .SumAsync(f => (decimal?)f.Valor) ?? 0m;

            // COMPLEMENTO A: a gaveta e fisica, entao so entram vendas em DINHEIRO.
            // Vendas em Credito/Debito/Pix nao deixam dinheiro no caixa.
            var vendasDinheiro = await _context.Vendas
                .Where(v => v.DataVenda.Date == hoje && v.MetodoPagamento == MetodoPagamentoDinheiro)
                .SumAsync(v => (decimal?)v.TotalVenda) ?? 0m;

            // Sangria ja e negativa no banco, entao entra por ADICAO (fonte unica CalcularSaldoEmGaveta).
            var saldoEmGaveta = CalcularSaldoEmGaveta(vendasDinheiro, suprimentos, sangrias);

            return Json(new
            {
                suprimento = (double)suprimentos,
                vendas = (double)vendasDinheiro,
                sangrias = (double)Math.Abs(sangrias),
                saldo = (double)saldoEmGaveta
            });
        }

        [HttpGet]
        public async Task<string> ObterUltimaVendaId()
        {
            var ultimaVenda = await _context.Vendas.OrderByDescending(v => v.Id).FirstOrDefaultAsync();
            return ultimaVenda?.Id.ToString() ?? "";
        }

        // --- CLASSES AUXILIARES ---

        /// <summary>
        /// REGRA ÚNICA de subtotal de item de venda (fonte de verdade para FinalizarVenda,
        /// cupom e ObterDetalhesVenda).
        /// O preço vem SEMPRE da tabela (produto.Preco), nunca do POST.
        /// Granel: o cadastro (Novo.cshtml) guarda o preço por 100 g e a quantidade em kg,
        /// então multiplica por 10 (R$ 5,00/100 g = R$ 50,00/kg x qtd em kg).
        /// </summary>
        public static decimal CalcularSubtotalItem(Produto produto, decimal quantidade)
        {
            decimal subtotal = produto.Preco * quantidade;

            if (produto.Tipo == TipoProduto.Granel)
                subtotal *= 10;

            return Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);
        }

        // ------------------------------------------------------------------
        // GAVETA: fonte unica de verdade do saldo em dinheiro.
        // Regra compartilhada por ObterStatusAtual, ObterDadosFechamento,
        // RelatorioDados e pela validacao de saldo do RegistrarMovimentacao.
        // mesmo criterio ja usado por ObterResumo (vendasDinheiro).
        // ------------------------------------------------------------------

        /// <summary>
        /// Unica forma de pagamento que ENTRA FISICAMENTE na gaveta do caixa.
        /// Vendas em Credito/Debito/Pix nao deixam dinheiro no caixa e por isso
        /// nao compoem o saldo da gaveta.
        /// </summary>
        public const string MetodoPagamentoDinheiro = "Dinheiro";

        /// <summary>
        /// Saldo fisico da gaveta: vendas em DINHEIRO + suprimentos + sangrias.
        /// ATENCAO AO SINAL: a sangria ja e gravada com valor NEGATIVO
        /// (-Math.Abs em RegistrarMovimentacao), por isso ela entra por ADICAO.
        /// Subtrair aqui inverteria o sinal e a sangria passaria a SOMAR ao saldo.
        /// </summary>
        public static decimal CalcularSaldoEmGaveta(decimal vendasDinheiro, decimal suprimentos, decimal sangrias)
            => vendasDinheiro + suprimentos + sangrias;

        public class ItemVendaSimples
        {
            public string ProdutoId { get; set; }
            public string Quantidade { get; set; }
            public string Preco { get; set; }
        }

        // Métodos de Histórico e Cupom (Mantidos conforme seu original)
        // Local: CaixaController.cs

        public async Task<IActionResult> Historico(DateTime? data)
        {
            // O Include(v => v.Itens) é essencial para o modal de detalhes funcionar!
            var query = _context.Vendas.Include(v => v.Itens).AsQueryable();

            if (data.HasValue)
            {
                query = query.Where(v => v.DataVenda.Date == data.Value.Date);
            }

            var listaVendas = await query.OrderByDescending(v => v.DataVenda).ToListAsync() ?? new List<Venda>();

            // Preenchendo as ViewBags com os nomes corretos das colunas
            ViewBag.FaturamentoSemana = await _context.Vendas
                .Where(v => v.DataVenda >= DateTime.Today.AddDays(-7))
                .SumAsync(v => v.TotalVenda);

            ViewBag.FaturamentoMes = await _context.Vendas
                .Where(v => v.DataVenda.Month == DateTime.Today.Month)
                .SumAsync(v => v.TotalVenda);

            ViewBag.FaturamentoAno = await _context.Vendas
                .Where(v => v.DataVenda.Year == DateTime.Today.Year)
                .SumAsync(v => v.TotalVenda);

            return View(listaVendas);
        }
        // Local: CaixaController.cs

        [HttpGet]
        public async Task<IActionResult> RelatorioDados()
        {
            // Usamos um intervalo de tempo em vez de apenas .Date para evitar erros de fuso horário
            var inicioDia = DateTime.Today;
            var fimDia = DateTime.Today.AddDays(1).AddTicks(-1);

            var movimentacoes = await _context.Set<CaixaMovimentacao>()
                .Where(m => m.Data >= inicioDia && m.Data <= fimDia)
                .OrderByDescending(m => m.Data)
                .ToListAsync() ?? new List<CaixaMovimentacao>();

            // COMPLEMENTO A: a gaveta e fisica, entao so entram vendas em DINHEIRO.
            // Vendas em Credito/Debito/Pix nao deixam dinheiro no caixa.
            var totalVendas = await _context.Vendas
                .Where(v => v.DataVenda >= inicioDia && v.DataVenda <= fimDia
                            && v.MetodoPagamento == MetodoPagamentoDinheiro)
                .SumAsync(v => (decimal?)v.TotalVenda) ?? 0m;

            // Filtros usando Equals para garantir que pegue exatamente o que você forçou no RegistrarMovimentacao
            var totalSuprimento = movimentacoes
                .Where(m => m.Tipo.Equals("Suprimento", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Valor);

            var totalSangria = movimentacoes
                .Where(m => m.Tipo.Equals("Sangria", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Valor);

            // Sangria ja e negativa no banco, entao entra por ADICAO (fonte unica CalcularSaldoEmGaveta).
            var saldoFinal = CalcularSaldoEmGaveta(totalVendas, totalSuprimento, totalSangria);

            return Json(new
            {
                movimentacoes = movimentacoes.Select(m => new {
                    hora = m.Data.ToString("HH:mm"), // Facilitar pro JS
                    tipo = m.Tipo,
                    valor = m.Valor,
                    descricao = m.Descricao
                }),
                totalVendas = (double)totalVendas,
                totalSuprimento = (double)totalSuprimento,
                totalSangria = (double)Math.Abs(totalSangria), // Mostra positivo no resumo da tela
                saldoFinal = (double)saldoFinal
            });
        }

        [HttpGet]
        public async Task<IActionResult> ObterResumo()
        {
            var hoje = DateTime.Today;

            var resumoPagamentos = await _context.Vendas
                .Where(v => v.DataVenda >= hoje)
                .GroupBy(v => v.MetodoPagamento)
                .Select(g => new { Metodo = g.Key, Total = g.Sum(v => v.TotalVenda) })
                .ToListAsync();

            // AJUSTADO PARA CaixaMovimentacoes
            var movimentacoes = await _context.CaixaMovimentacoes
                .Where(m => m.Data >= hoje)
                .ToListAsync();

            var totalSuprimento = movimentacoes.Where(m => m.Tipo == "Suprimento").Sum(m => m.Valor);
            var totalSangria = movimentacoes.Where(m => m.Tipo == "Sangria").Sum(m => m.Valor);

            var vendasDinheiro = resumoPagamentos.FirstOrDefault(p => p.Metodo == "Dinheiro")?.Total ?? 0;

            // Matemática ajustada para valor negativo
            var saldoDinheiroEsperado = vendasDinheiro + totalSuprimento + totalSangria;

            return Json(new
            {
                pagamentos = resumoPagamentos,
                suprimento = totalSuprimento,
                sangria = Math.Abs(totalSangria),
                saldoDinheiro = saldoDinheiroEsperado,
                totalGeral = resumoPagamentos.Sum(p => p.Total) + totalSuprimento + totalSangria
            });
        }
        // Rota que o JavaScript chama: /Caixa/GerarCupom?id=XX
        [HttpGet]
        public IActionResult GerarCupom(int id)
        {
            var venda = _context.Vendas
                .Include(v => v.Itens)
                .ThenInclude(i => i.Produto)
                .FirstOrDefault(v => v.Id == id);

            if (venda == null) return NotFound();

            return View(venda);
        }

        public IActionResult ImprimirRelatorioHoje()
        {
            // Pega o início e o fim do dia de hoje
            var hoje = DateTime.Today;
            var amanha = hoje.AddDays(1);

            var vendas = _context.Vendas
                .Include(v => v.Itens)
                .ThenInclude(i => i.Produto)
                .Where(v => v.DataVenda >= hoje && v.DataVenda < amanha)
                .ToList();

            return View(vendas);
        }

        [HttpGet]
        public JsonResult ObterRelatorioResumido()
        {
            try
            {
                DateTime hoje = DateTime.Today;

                // 1. Vendas (Usando DataVenda e TotalVenda)
                var vendas = _context.Vendas
                    .Where(v => v.DataVenda.Date == hoje)
                    .Select(v => new {
                        hora = v.DataVenda.ToString("HH:mm"),
                        descricao = "Venda #" + v.Id + " (" + (v.MetodoPagamento ?? "N/A") + ")",
                        valor = (double)v.TotalVenda
                    }).ToList();

                // 2. Movimentações (Usando Data e Descricao)
                var movimentacoes = _context.CaixaMovimentacoes
                    .Where(m => m.Data.Date == hoje)
                    .Select(m => new {
                        hora = m.Data.ToString("HH:mm"),
                        descricao = m.Tipo + (string.IsNullOrEmpty(m.Descricao) ? "" : " - " + m.Descricao),
                        valor = (double)m.Valor
                    }).ToList();

                // Une e ordena
                var todosDados = vendas.Concat(movimentacoes)
                    .OrderBy(x => x.hora)
                    .ToList();

                return Json(new { movimentacoes = todosDados });
            }
            catch (Exception ex)
            {
                return Json(new { erro = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult ObterDadosFechamento()
        {
            try
            {
                DateTime hoje = DateTime.Today;

                // FILTRO CRÍTICO: v.DataVenda.Date == hoje && !v.IsFechado
                // COMPLEMENTO A: a gaveta e fisica, entao so entram vendas em DINHEIRO.
                // Vendas em Credito/Debito/Pix nao deixam dinheiro no caixa.
                decimal totalVendas = _context.Vendas
                    .Where(v => v.DataVenda.Date == hoje && !v.IsFechado
                                && v.MetodoPagamento == MetodoPagamentoDinheiro)
                    .Sum(v => (decimal?)v.TotalVenda) ?? 0;

                decimal suprimentos = _context.CaixaMovimentacoes
                    .Where(m => m.Data.Date == hoje && m.Tipo == "Suprimento" && !m.IsFechado)
                    .Sum(m => (decimal?)m.Valor) ?? 0;

                decimal sangrias = _context.CaixaMovimentacoes
                    .Where(m => m.Data.Date == hoje && m.Tipo == "Sangria" && !m.IsFechado)
                    .Sum(m => (decimal?)m.Valor) ?? 0;

                // BUG-01 (sinal da sangria): a sangria ja e gravada NEGATIVA, entao
                // entra por ADICAO. Antes era (totalVendas + suprimentos) - sangrias,
                // que invertia o sinal e fazia a sangria SOMAR ao saldo.
                decimal saldoFinal = CalcularSaldoEmGaveta(totalVendas, suprimentos, sangrias);

                // Detalhamento de estoque (apenas do que ainda não foi fechado)
                var produtosSaida = _context.VendaItens
                    .Where(vi => vi.Venda.DataVenda.Date == hoje && !vi.Venda.IsFechado)
                    .GroupBy(vi => vi.Produto.Nome)
                    .Select(g => new {
                        produto = g.Key,
                        quantidade = g.Sum(x => x.Quantidade)
                    }).ToList();

                // Histórico de vendas (apenas do que ainda não foi fechado)
                var historicoVendas = _context.Vendas
                .Where(v => v.DataVenda.Date == hoje && !v.IsFechado)
                .OrderByDescending(v => v.DataVenda)
                .Select(v => new {
                hora = v.DataVenda.ToString("HH:mm"),
        // Buscamos os itens da venda pelo ID dela
                itens = string.Join(", ", _context.VendaItens
                    .Where(vi => vi.VendaId == v.Id)
                    .Select(vi => vi.Produto.Nome)),
                total = v.TotalVenda
    })
                .ToList();

                return Json(new
                {
                    totalVendas,
                    suprimentos,
                    sangrias,
                    saldoFinal,
                    produtosSaida,
                    historicoVendas
                });
            }
            catch (Exception ex)
            {
                return Json(new { erro = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObterDetalhesVenda(int id)
        {
            var venda = await _context.Vendas
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venda == null) return NotFound();

            var itens = await _context.VendaItens
                .Include(i => i.Produto)
                .Where(i => i.VendaId == id)
                .Select(i => new {
                    produto = i.Produto.Nome,
                    quantidade = i.Quantidade,
                    preco = i.PrecoUnitario,
                    total = i.PrecoUnitario * i.Quantidade // Cálculo em tempo real
                }).ToListAsync();

            return Json(new
            {
                vendedor = venda.CodigoVendedor ?? "Identificado",
                itens = itens // Aqui 'itens' deve ter a propriedade 'total' calculada
            });
        }

        [HttpPost]
        public IActionResult ZerarCaixaParaProximoDia()
        {
            try
            {
                DateTime hoje = DateTime.Today;

                // 1. Marca as Vendas como fechadas
                var vendas = _context.Vendas
                    .Where(v => v.DataVenda.Date == hoje && !v.IsFechado).ToList();
                vendas.ForEach(v => v.IsFechado = true);

                // 2. Marca as Movimentações (Sangria/Suprimento) como fechadas
                var movs = _context.CaixaMovimentacoes
                    .Where(m => m.Data.Date == hoje && !m.IsFechado).ToList();
                movs.ForEach(m => m.IsFechado = true);

                _context.SaveChanges(); // ESSA LINHA É O QUE "ZERA" O BANCO

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // 1.1 Cadastro de Vendedor
        [HttpPost]
        public async Task<IActionResult> CadastrarVendedor(string nome, string codigo)
        {
            if (await _context.Vendedores.AnyAsync(v => v.Codigo == codigo))
                return Json(new { success = false, message = "Código já existe!" });

            _context.Vendedores.Add(new Vendedor { Nome = nome, Codigo = codigo, Ativo = true });
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 1.2 Ver Vendas por Vendedor (Para a tela "Ver Cupom")
        public async Task<IActionResult> VerVendasVendedor(string codigo)
        {
            var vendas = await _context.Vendas
                .Where(v => v.CodigoVendedor == codigo && v.DataVenda.Date == DateTime.Today)
                .Select(v => new { v.Id, Total = v.TotalVenda, v.DataVenda })
                .ToListAsync();
            return Json(vendas);
        }

        

        [HttpGet]
        public async Task<IActionResult> ValidarCupom(string codigo)
        {
            if (string.IsNullOrEmpty(codigo))
                return Json(new { exists = false });

            // Verifica se existe algum vendedor com esse código e que esteja ativo
            var existe = await _context.Vendedores
                .AnyAsync(v => v.Codigo.ToUpper() == codigo.ToUpper() && v.Ativo);

            return Json(new { exists = existe });
        }

        [HttpGet]
        public async Task<IActionResult> ListarVendedores()
        {
            try
            {
                // Busca apenas os ativos para não poluir a lista
                var lista = await _context.Vendedores
                    .Where(v => v.Ativo)
                    .OrderBy(v => v.Nome)
                    .ToListAsync();

                return Json(lista);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Erro ao buscar vendedores: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExcluirVendedor(string codigo)
        {
            var vendedor = await _context.Vendedores.FirstOrDefaultAsync(v => v.Codigo == codigo);
            if (vendedor == null) return Json(new { success = false, message = "Vendedor não encontrado." });

            vendedor.Ativo = false; // Desativa em vez de deletar para manter integridade
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> BuscarPorId(int id)
        {
            // O AsNoTracking() obriga o Entity Framework a ler o arquivo .db agora, 
            // ignorando qualquer "cache" ou memória antiga.
            var p = await _context.Produtos
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (p == null) return Json(new { success = false });

            return Json(new
            {
                success = true,
                id = p.Id,
                nome = p.Nome,
                preco = p.Preco,
                regra = p.Tipo.ToString().ToUpper(),
                estoque = p.Quantidade // Aqui deve retornar 4 se estiver no banco
            });
        }

        [HttpGet]
        public async Task<IActionResult> BuscarPorCodigo(string codigo)
        {
            var p = await _context.Produtos
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CodigoBarras == codigo || x.Id.ToString() == codigo);

            if (p == null) return Json(new { success = false });

            return Json(new
            {
                success = true,
                id = p.Id,
                nome = p.Nome,
                preco = p.Preco,
                // Garante que o nome enviado seja 'estoque' para o seu JS não se perder
                estoque = p.Quantidade
            });
        }

    }
}