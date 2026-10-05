using Microsoft.AspNetCore.Authorization;
using ControleEstoqueLoja;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;

namespace ControleEstoqueLoja.Controllers
{
    [Authorize]
    public class CaixaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CaixaController(ApplicationDbContext context, IConfiguration? config = null)
        {
            _context = context;

            // Mesmo contrato do HomeController: parametro OPCIONAL no fim, para
            // `new CaixaController(qa.Context)` da suite de QA continuar valendo
            // sem alteracao nenhuma nos testes existentes.
            Marca = LerTextoDeConfig(config, "Marca:Nome", "ControlHub");
            Loja = LerTextoDeConfig(config, "Loja:Nome", string.Empty);
        }

        /// <summary>Rotulo do topo da planilha (`Marca:Nome`). Padrao: ControlHub.</summary>
        protected string Marca { get; }

        /// <summary>Rotulo da loja (`Loja:Nome`). Padrao: vazio (nao aparece).</summary>
        protected string Loja { get; }

        /// <summary>
        /// Le uma chave de configuracao e devolve o padrao quando ela nao existe
        /// ou esta em branco (chave presente e valor "" conta como ausente).
        /// </summary>
        private static string LerTextoDeConfig(IConfiguration? config, string chave, string padrao)
        {
            var valor = config?[chave]?.Trim();
            return string.IsNullOrWhiteSpace(valor) ? padrao : valor;
        }

        [Authorize(Policy = Policies.PodeOperarPDV)]
        public async Task<IActionResult> Index()
        {
            // 1. Busca os produtos ATIVOS no banco de dados
            // (BUG-08: produtos desativados por soft delete nao aparecem no PDV)
            var produtos = await _context.Produtos.Where(p => p.Ativo).ToListAsync();

            // 2. Alimenta a ViewBag que o seu HTML (o @foreach) está procurando
            ViewBag.Produtos = produtos;

            // 3. Mantém o contador caso você use em algum lugar da tela
            ViewBag.TotalProdutos = produtos.Count;

            // 4. Retorna a lista para a View (Model principal)
            return View(produtos);
        }

        // --- MÉTODOS DE FINALIZAÇÃO E ESTOQUE ---

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeOperarPDV)]
        public async Task<IActionResult> FinalizarVenda(string metodoPagamento, string? cpfCliente, string codigoVendedor, decimal valorRecebido, decimal valorTroco, List<ItemVendaSimples> itens)
        {
            // CUPOM DO VENDEDOR E OPCIONAL (programa de recompensa por venda):
            // valida somente quando informado; sem cupom a venda segue com o
            // vendedor nao identificado (CodigoVendedor = null). BUG-18 mantido:
            // valida e finaliza com o MESMO criterio (NormalizarCupom), senao o
            // cupom validado seria rejeitado na venda.
            var cupomNormalizado = NormalizarCupom(codigoVendedor);
            Vendedor? vendedor = null;
            if (!string.IsNullOrEmpty(cupomNormalizado))
            {
                vendedor = await _context.Vendedores
                    .FirstOrDefaultAsync(v => v.Codigo.ToUpper() == cupomNormalizado && v.Ativo);

                if (vendedor == null)
                {
                    return Json(new { success = false, message = $"O cupom '{codigoVendedor}' não foi encontrado!" });
                }
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
                // Grava normalizado (caixa unica) quando informado; null quando
                // a venda foi fechada sem cupom (opcional).
                CodigoVendedor = string.IsNullOrEmpty(cupomNormalizado) ? null : cupomNormalizado,
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

                // BUG-08: um produto desativado (soft delete) nao pode mais ser vendido.
                var produto = await _context.Produtos.FindAsync(idProd);

                if (produto == null || !produto.Ativo)
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

                    // Produto vendido por peso aceita quantidade fracionária em kg.
                    if (produto.Tipo != TipoProduto.PorPeso && qtdVendida != decimal.Truncate(qtdVendida))
                    {
                        return Json(new { success = false, message = $"Quantidade fracionada não permitida para {produto.Nome}." });
                    }

                    // 2. VALIDAÇÃO DE ESTOQUE COM MENSAGEM DETALHADA
                    if (produto.Quantidade < qtdVendida)
                    {
                        string unidadeMedida = produto.Tipo == TipoProduto.PorPeso ? "kg" : "un";

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
                    // Para venda por peso, produto.Preco já está em R$/kg.
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
            // BUG-29: dinheiro insuficiente, 3 casas decimais ou valor fora da faixa
            // armazenável são recusados AQUI, com o total já conhecido (regra 5: a fórmula
            // do total fica no loop, não é duplicada aqui). Como o loop JÁ baixou o estoque,
            // a rejeição precisa devolver o contexto ao estado anterior: sem isto os
            // produtos percorridos ficariam baixados na instância do DbContext.
            if (metodoPagamento.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase))
            {
                var motivoDinheiro = ValidarValorRecebido(valorRecebido, novaVenda.TotalVenda);
                if (motivoDinheiro != null)
                {
                    // Descarta as baixas de estoque pendentes (BUG-29): sem transação explícita,
                    // o jeito de não deixar rastro é não salvar e soltar as entidades rastreadas.
                    _context.ChangeTracker.Clear();
                    return Json(new { success = false, message = motivoDinheiro });
                }

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

        /// <summary>
        /// BUG-29: valida o valor recebido em uma venda em DINHEIRO. Devolve null quando
        /// o valor pode ser gravado, ou a mensagem de recusa (mesmo formato da action:
        /// HTTP 200 com success = false).
        ///
        /// Três recusas, todas ANTES de gravar, para que nenhum caso termine em 500:
        ///  1. mais de 2 casas decimais - dinheiro não tem fração de centavo, e ARREDONDAR
        ///     aqui mudaria um valor que o operador digitou (26,995 viraria 27,00 e a venda
        ///     passaria a aceitar 26,995 como se fossem 27,00);
        ///  2. acima da faixa que ConversorCentavos aceita - lá o estouro é um
        ///     OverflowException dentro do SaveChanges, isto é, um 500;
        ///  3. insuficiente - o troco sairia negativo, que é um registro que nenhum
        ///     humano produziu (significa "o caixa deve ao cliente").
        /// </summary>
        private static string? ValidarValorRecebido(decimal valorRecebido, decimal totalVenda)
        {
            // (1) 3 casas ou mais: mesma regra de ConversorCentavos.ParaCentavos.
            if (decimal.Round(valorRecebido, 2) != valorRecebido)
            {
                return "Valor recebido inválido: use no máximo 2 casas decimais (centavos).";
            }

            // (2) Faixa armazenável: o teto é o de ConversorCentavos (não inventado aqui).
            if (Math.Abs(valorRecebido) > (decimal)ConversorCentavos.MaxCentavos / 100m)
            {
                return "Valor recebido fora da faixa aceita.";
            }

            // (3) Insuficiente: o troco é recalculado no servidor (regra 6).
            if (valorRecebido < totalVenda)
            {
                var falta = (totalVenda - valorRecebido).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
                return $"Valor recebido insuficiente. Faltam R$ {falta}.";
            }

            return null;
        }

        // --- MÉTODOS DO MENU ADMINISTRATIVO ---

        /// <summary>
        /// SUPRIMENTO: dinheiro que ENTRA na gaveta (abertura de turno, troco).
        /// Liberado para Operador e Dono - e operacao de balcao, nao
        /// movimenta dinheiro para fora da loja.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeRegistrarSuprimento)]
        public IActionResult RegistrarSuprimento(string valor, string obs)
            => RegistrarMovimentacaoInterno("Suprimento", valor, obs, validarSaldo: false);

        /// <summary>
        /// SANGRIA: dinheiro que SAI da gaveta. Somente Dono - e a operacao que
        /// permite retirada de dinheiro da loja, entao exige perfil gerencial.
        ///
        /// A action antiga "RegistrarMovimentacao" (unica, com o tipo no corpo)
        /// foi dividida em duas: assim a permissao fica na declaracao da rota
        /// (policy) e nao escondida em um if(User.IsInRole(...)) dentro do
        /// metodo. Um Operador consegue chamar /RegistrarSangria e leva 403.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeRegistrarSangria)]
        public IActionResult RegistrarSangria(string valor, string obs)
            => RegistrarMovimentacaoInterno("Sangria", valor, obs, validarSaldo: true);

        /// <summary>
        /// Logica compartilhada por suprimento e sangria. O tipo vem da ACTION
        /// (nao do POST): assim nao existe rota que aceite os dois tipos.
        /// </summary>
        private IActionResult RegistrarMovimentacaoInterno(string tipo, string valor, string obs, bool validarSaldo)
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

                bool ehSangria = tipo == "Sangria";

                // BUG-05: sem esta validacao a gaveta pode ficar negativa: o caixa
                // retirava mais dinheiro do que havia. Valida contra o saldo real,
                // considerando SO vendas em Dinheiro (fonte unica CalcularSaldoEmGaveta).
                if (validarSaldo && ehSangria)
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
                // BUG-12 (vazamento de ex.Message): a mensagem interna (SqliteException,
                // nome de tabela/coluna, caminho de arquivo) ia para o cliente. O
                // operador nao precisa dela e ela vaza informacao de infraestrutura.
                LogarExcecao(nameof(RegistrarMovimentacaoInterno), ex);
                return Json(new { success = false, message = MensagemErroGenerica });
            }
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeFecharCaixa)]
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
        [Authorize(Policy = Policies.PodeOperarPDV)]
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
        /// Para venda por peso, o cadastro guarda o preço em R$/kg e a quantidade em kg.
        /// </summary>
        public static decimal CalcularSubtotalItem(Produto produto, decimal quantidade)
        {
            return Math.Round(produto.Preco * quantidade, 2, MidpointRounding.AwayFromZero);
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

        // ------------------------------------------------------------------
        // BUG-12: tratamento de erro sem vazamento de informacao.
        //
        // Antes, cada catch devolvia ex.Message (ou InnerException.Message)
        // direto no JSON. Isso expunha ao cliente nomes de tabelas/colunas,
        // caminhos de arquivo e tipos de excecao (SqliteException,
        // FormatException) - informacao de infraestrutura nao pertencente a
        // quem opera o caixa.
        //
        // A excecao real vai para o log do servidor (Console.Error, ja que
        // ainda nao ha ILogger configurado - ver item futuro) e o cliente
        // recebe uma mensagem generica, sem detalhe tecnico.
        // ------------------------------------------------------------------

        /// <summary>Mensagem devolvida ao cliente quando o erro nao e previsto.</summary>
        public const string MensagemErroGenerica = "Erro ao processar a solicitação.";

        /// <summary>
        /// Registra a excecao REAL no log do servidor. Nao usar ILogger ainda:
        /// o projeto nao configura logging; o Console.Error aparece no log da
        /// aplicacao (dotnet run / IISExpress / servico), que e quem pode
        /// diagnosticar. O operador ve apenas MensagemErroGenerica.
        /// </summary>
        private static void LogarExcecao(string origem, Exception ex)
        {
            Console.Error.WriteLine(
                $"[ERRO] {origem}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex}");
        }

        /// <summary>
        /// Normaliza o CUPOM (código do vendedor) em UM ÚNICO formato.
        /// BUG-18: ValidarCupom comparava com ToUpper() nos dois lados (case-insensitive),
        /// mas FinalizarVenda comparava direto com "=". No SQLite o "=" é case-SENSITIVE
        /// (collation BINARY), então o cupom "CUPOM1" era aceito na validação como "cupom1"
        /// e a venda era REJEITADA logo em seguida por "cupom não encontrado".
        /// Trim remove espaços acidentais; ToUpper unifica a caixa.
        /// </summary>
        public static string NormalizarCupom(string? codigo)
            => (codigo ?? string.Empty).Trim().ToUpper();

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
            // Anulaiveis: chegam do POST e o model binding deixa em branco o que o
            // formulario nao envia. Todo o consumo ja trata null (TryParse / ?.) —
            // declarar como string? apenas reconhece a realidade (elimina CS8618).
            public string? ProdutoId { get; set; }
            public string? Quantidade { get; set; }
            public string? Preco { get; set; }
        }

        // Métodos de Histórico e Cupom (Mantidos conforme seu original)
        // Local: CaixaController.cs

        [Authorize(Policy = Policies.PodeVerFinanceiro)]
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

        /// <summary>
        /// FASE A - ITEM 3: exporta o historico de vendas em CSV.
        ///
        /// REGRA DE OURO: respeita o MESMO filtro de dia da Historico. Se o campo
        /// de data esta em 10/02/2026, o CSV tem SÓ esse dia - exportar "tudo"
        /// enquanto a tela mostra um dia so seria o tipo de erro que faz o dono
        /// fechar o mes com o numero errado.
        ///
        /// NAO e dado novo: nao grava, nao recalcula total, nao mexe no caixa.
        /// Por isso nao cria migration.
        ///
        /// O TotalVenda vai como esta gravado (vindo do ConversorCentavos, em
        /// centavos), sem recalcular a partir dos itens: se someday algum dia o
        /// total da tela for ajustado a mao, o CSV continua mostrando o que o
        /// caixa realmente fechou.
        ///
        /// Same policy da Historico (PodeVerFinanceiro) e pelo mesmo motivo:
        /// exportacao de venda e dado financeiro tanto quanto a tela.
        /// </summary>
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
        public async Task<IActionResult> ExportarHistoricoCsv(DateTime? data)
        {
            // Mesmo filtro da Historico, para export e tela nunca divergirem.
            var query = _context.Vendas.Include(v => v.Itens).AsQueryable();

            if (data.HasValue)
            {
                query = query.Where(v => v.DataVenda.Date == data.Value.Date);
            }

            var vendas = await query.OrderByDescending(v => v.DataVenda).ToListAsync() ?? new List<Venda>();

            var cabecalhos = new[]
            {
                "Venda", "Data", "Itens", "Quantidade Total", "Metodo Pagamento",
                "Vendedor (Cupom)", "Valor Recebido", "Troco", "Total (R$)"
            };

            var linhas = vendas
                .Select(v => (IReadOnlyList<string>)new[]
                {
                    v.Id.ToString(CultureInfo.InvariantCulture),
                    ExportacaoCsv.DataHora(v.DataVenda),

                    // Quantos itens a venda levou e o total de quantidade,
                    // como NUMEROS (e nao como lista de nomes): sao as duas
                    // colunas que o dono usa para conferir a venda. O detalhe
                    // item a item fica com o nome em outro relatorio - aqui ele
                    // viraria um texto dentro da celula, que o Excel nao
                    // consegue somar nem filtrar.
                    v.Itens.Count.ToString(CultureInfo.InvariantCulture),
                    ExportacaoCsv.Quantidade(v.Itens.Sum(i => i.Quantidade)),

                    // Texto do PDV (metodo e cupom) vem de campo livre do
                    // usuario: passa pelo escudo de CSV injection.
                    ExportacaoCsv.EscaparTexto(v.MetodoPagamento),
                    ExportacaoCsv.EscaparTexto(v.CodigoVendedor),

                    ExportacaoCsv.Dinheiro(v.ValorRecebido),
                    ExportacaoCsv.Dinheiro(v.ValorTroco),
                    ExportacaoCsv.Dinheiro(v.TotalVenda)
                })
                .ToList();

            // No nome do arquivo o dia vai junto: quem baixa cinco exportacoes
            // nao precisa abrir cada uma para saber de que dia era.
            var sufixo = data.HasValue ? data.Value.ToString("yyyy-MM-dd") : "completo";
            var nomeArquivo = $"vendas-{sufixo}.csv";

            // Content-Disposition e o que faz o navegador BAIXAR em vez de
            // tentar abrir o csv na propria aba.
            var arquivo = ExportacaoCsv.Montar(cabecalhos, linhas);
            return File(arquivo, "text/csv; charset=utf-8", nomeArquivo);
        }

        /// <summary>
        /// ETAPA A (ControlHub): o MESMO historico de vendas, agora em Excel
        /// (.xlsx) - ao lado do CSV, sem substitui-lo.
        ///
        /// Mesma policy do CSV (PodeVerFinanceiro) e pelo mesmo motivo: exportacao
        /// de venda e dado financeiro tanto quanto a tela e o CSV.
        ///
        /// Mesma regra de ouro: respeita o filtro de dia da Historico. E o
        /// TotalVenda continua saindo como esta GRAVADO, sem recalcular a partir
        /// dos itens.
        ///
        /// BUG-12/16/17: falha na geracao vai para o log do servidor (com a
        /// excecao real); o operador recebe so <see cref="MensagemErroGenerica"/>,
        /// sem nome de tabela nem caminho de arquivo.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
        public async Task<IActionResult> ExportarHistoricoExcel(DateTime? data)
        {
            try
            {
                // Mesmo filtro da Historico, para export e tela nunca divergirem.
                var query = _context.Vendas.Include(v => v.Itens).AsQueryable();

                if (data.HasValue)
                {
                    query = query.Where(v => v.DataVenda.Date == data.Value.Date);
                }

                var vendas = await query.OrderByDescending(v => v.DataVenda).ToListAsync() ?? new List<Venda>();

                // No nome do arquivo o dia vai junto (ou "completo"): quem baixa cinco
                // exportacoes nao precisa abrir cada uma para saber de que dia era.
                var sufixo = data.HasValue ? data.Value.ToString("yyyy-MM-dd") : "completo";

                var conteudo = ExportacaoExcel.Historico(vendas, Marca, Loja, data);
                return File(conteudo, ExportacaoExcel.ContentType, $"vendas-{sufixo}.xlsx");
            }
            catch (Exception ex)
            {
                LogarExcecao(nameof(ExportarHistoricoExcel), ex);
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
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
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
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
        [Authorize(Policy = Policies.PodeReimprimirCupom)]
        public IActionResult GerarCupom(int id)
        {
            var venda = _context.Vendas
                .Include(v => v.Itens)
                .ThenInclude(i => i.Produto)
                .FirstOrDefault(v => v.Id == id);

            if (venda == null) return NotFound();

            return View(venda);
        }

        [Authorize(Policy = Policies.PodeVerFinanceiro)]
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
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
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
                // BUG-12 (vazamento de ex.Message): log no servidor, resposta generica.
                LogarExcecao(nameof(ObterRelatorioResumido), ex);
                return Json(new { success = false, erro = MensagemErroGenerica });
            }
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
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
                // BUG-12 (vazamento de ex.Message): log no servidor, resposta generica.
                LogarExcecao(nameof(ObterDadosFechamento), ex);
                return Json(new { success = false, erro = MensagemErroGenerica });
            }
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeOperarPDV)]
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
                    unidade = i.Produto.Tipo == TipoProduto.PorPeso ? "kg" : "un",
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

        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeFecharCaixa)]
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
                // BUG-12 (vazamento de ex.Message): log no servidor, resposta generica.
                LogarExcecao(nameof(ZerarCaixaParaProximoDia), ex);
                return Json(new { success = false, message = MensagemErroGenerica });
            }
        }

        // 1.1 Cadastro de Vendedor
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarVendedores)]
        public async Task<IActionResult> CadastrarVendedor(string nome, string codigo)
        {
            // BUG-18: grava sempre o cupom NORMALIZADO e compara duplicado pelo mesmo
            // critério das demais rotas, para que o código digitado de formas diferentes
            // (maiúscula/minúscula/espaços) seja sempre reconhecido.
            var cupom = NormalizarCupom(codigo);

            if (string.IsNullOrWhiteSpace(cupom))
                return Json(new { success = false, message = "Código do cupom é obrigatório." });

            if (await _context.Vendedores.AnyAsync(v => v.Codigo.ToUpper() == cupom))
                return Json(new { success = false, message = "Código já existe!" });

            _context.Vendedores.Add(new Vendedor
            {
                Nome = nome?.Trim() ?? string.Empty,
                Codigo = cupom,
                Ativo = true
            });
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 1.2 Ver Vendas por Vendedor (Para a tela "Ver Cupom")
        [Authorize(Policy = Policies.PodeVerFinanceiro)]
        public async Task<IActionResult> VerVendasVendedor(string codigo)
        {
            var vendas = await _context.Vendas
                .Where(v => v.CodigoVendedor == NormalizarCupom(codigo) && v.DataVenda.Date == DateTime.Today)
                .Select(v => new { v.Id, Total = v.TotalVenda, v.DataVenda })
                .ToListAsync();
            return Json(vendas);
        }

        

        [HttpGet]
        [Authorize(Policy = Policies.PodeOperarPDV)]
        public async Task<IActionResult> ValidarCupom(string codigo)
        {
            if (string.IsNullOrEmpty(codigo))
                return Json(new { exists = false });

            // Verifica se existe algum vendedor com esse código e que esteja ativo.
            // Mesmo critério (NormalizarCupom) usado no FinalizarVenda.
            var existe = await _context.Vendedores
                .AnyAsync(v => v.Codigo.ToUpper() == NormalizarCupom(codigo) && v.Ativo);

            return Json(new { exists = existe });
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeOperarPDV)]
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
                // BUG-12 (vazamento de ex.Message): a mensagem do SqliteException
                // ("no such table: Vendedores") revelava o schema do banco.
                LogarExcecao(nameof(ListarVendedores), ex);
                return BadRequest(new { success = false, message = MensagemErroGenerica });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarVendedores)]
        public async Task<IActionResult> ExcluirVendedor(string codigo)
        {
            var cupom = NormalizarCupom(codigo);
            var vendedor = await _context.Vendedores.FirstOrDefaultAsync(v => v.Codigo.ToUpper() == cupom);
            if (vendedor == null) return Json(new { success = false, message = "Vendedor não encontrado." });

            vendedor.Ativo = false; // Desativa em vez de deletar para manter integridade
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeOperarPDV)]
        public async Task<IActionResult> BuscarPorId(int id)
        {
            // O AsNoTracking() obriga o Entity Framework a ler o arquivo .db agora, 
            // ignorando qualquer "cache" ou memória antiga.
            var p = await _context.Produtos
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.Ativo);

            if (p == null) return Json(new { success = false });

            return Json(new
            {
                success = true,
                id = p.Id,
                nome = p.Nome,
                preco = p.Preco,
                regra = p.Tipo.ToString().ToUpper(),
                unidade = p.Tipo == TipoProduto.PorPeso ? "kg" : "un",
                estoque = p.Quantidade // Aqui deve retornar 4 se estiver no banco
            });
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeOperarPDV)]
        public async Task<IActionResult> BuscarPorCodigo(string codigo)
        {
            var p = await _context.Produtos
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Ativo && (x.CodigoBarras == codigo || x.Id.ToString() == codigo));

            if (p == null) return Json(new { success = false });

            return Json(new
            {
                success = true,
                id = p.Id,
                nome = p.Nome,
                preco = p.Preco,
                regra = p.Tipo.ToString().ToUpper(),
                unidade = p.Tipo == TipoProduto.PorPeso ? "kg" : "un",
                // Garante que o nome enviado seja 'estoque' para o seu JS não se perder
                estoque = p.Quantidade
            });
        }

    }
}
