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

            if (itens == null || !itens.Any()) return BadRequest();

            var novaVenda = new Venda
            {
                DataVenda = DateTime.Now,
                MetodoPagamento = metodoPagamento,
                CPFCliente = cpfCliente,
                CodigoVendedor = codigoVendedor,
                TotalVenda = 0,
                Itens = new List<VendaItem>()
            };

            foreach (var item in itens)
            {
                if (!int.TryParse(item.ProdutoId, out int idProd)) continue;

                var produto = await _context.Produtos.FindAsync(idProd);

                if (produto != null)
                {
                    // 1. Conversão dos valores vindos do front-end
                    decimal qtdVendida = decimal.Parse(item.Quantidade?.Replace(",", ".") ?? "0", CultureInfo.InvariantCulture);
                    decimal precoUnit = decimal.Parse(item.Preco?.Replace(",", ".") ?? "0", CultureInfo.InvariantCulture);

                    // 2. VALIDAÇÃO DE ESTOQUE COM MENSAGEM DETALHADA
                    if (produto.Quantidade < qtdVendida)
                    {
                        string unidadeMedida = (produto.Tipo.ToString().ToUpper() == "PESO" || produto.Tipo.ToString().ToUpper() == "GRANEL") ? "kg" : "un";

                        // Retorna o erro parando o loop e avisando o vendedor exatamente o saldo
                        return Json(new
                        {
                            success = false,
                            message = $"Estoque insuficiente para {produto.Nome}. \n" +
                                      $"Tentativa de venda: {qtdVendida}{unidadeMedida}. \n" +
                                      $"Disponível em estoque: {produto.Quantidade}{unidadeMedida}."
                        });
                    }

                    // 3. REGRA GRANEL (100g): O preço base é por 100g, então multiplica por 10 para o valor do kg
                    decimal subtotalItem;
                    string tipo = produto.Tipo.ToString().ToUpper();

                    if (tipo == "PESO" || tipo == "GRANEL")
                    {
                        // Se o preço cadastrado é R$ 5,00 (referente a 100g), o kg custa R$ 50,00.
                        subtotalItem = (precoUnit * 10) * qtdVendida;
                    }
                    else
                    {
                        subtotalItem = precoUnit * qtdVendida;
                    }

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

                var movimentacao = new CaixaMovimentacao
                {
                    Data = DateTime.Now,
                    // Forçamos o nome padrão aqui para o relatório nunca falhar
                    Tipo = tipo.Contains("Sangria", StringComparison.OrdinalIgnoreCase) ? "Sangria" : "Suprimento",
                    Valor = tipo.Contains("Sangria", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(valorDecimal) : Math.Abs(valorDecimal),
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

            var totalVendas = await _context.Vendas
                .Where(v => v.DataVenda.Date == hoje)
                .SumAsync(v => (decimal?)v.TotalVenda) ?? 0m;

            // Como salvamos Sangria negativa, aqui somamos para subtrair
            var saldoEmGaveta = totalVendas + suprimentos + sangrias;

            return Json(new
            {
                suprimento = (double)suprimentos,
                vendas = (double)totalVendas,
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

            var totalVendas = await _context.Vendas
                .Where(v => v.DataVenda >= inicioDia && v.DataVenda <= fimDia)
                .SumAsync(v => (decimal?)v.TotalVenda) ?? 0m;

            // Filtros usando Equals para garantir que pegue exatamente o que você forçou no RegistrarMovimentacao
            var totalSuprimento = movimentacoes
                .Where(m => m.Tipo.Equals("Suprimento", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Valor);

            var totalSangria = movimentacoes
                .Where(m => m.Tipo.Equals("Sangria", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Valor);

            // Como Sangria já é negativa no banco (-50), somamos para subtrair: 100 + (-50) = 50
            var saldoFinal = totalVendas + totalSuprimento + totalSangria;

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
                decimal totalVendas = _context.Vendas
                    .Where(v => v.DataVenda.Date == hoje && !v.IsFechado)
                    .Sum(v => (decimal?)v.TotalVenda) ?? 0;

                decimal suprimentos = _context.CaixaMovimentacoes
                    .Where(m => m.Data.Date == hoje && m.Tipo == "Suprimento" && !m.IsFechado)
                    .Sum(m => (decimal?)m.Valor) ?? 0;

                decimal sangrias = _context.CaixaMovimentacoes
                    .Where(m => m.Data.Date == hoje && m.Tipo == "Sangria" && !m.IsFechado)
                    .Sum(m => (decimal?)m.Valor) ?? 0;

                decimal saldoFinal = (totalVendas + suprimentos) - sangrias;

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