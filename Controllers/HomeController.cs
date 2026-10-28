using ControleEstoqueLoja.Data;
using Microsoft.AspNetCore.Authorization;
using ControleEstoqueLoja;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Linq;

namespace ControleEstoqueLoja.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        // Injeção do Banco de Dados
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context, IConfiguration? config = null)
        {
            _context = context;

            // MARCA e LOJA sao apenas rotulo de apresentacao no topo do .xlsx.
            // O parametro e OPCIONAL de proposito: a suite de QA constroi
            // `new HomeController(qa.Context)` e continua compilando sem saber
            // que a planilha tem cabecalho de marca. Quando a configuracao nao
            // existe (teste ou ambiente sem appsettings), cai no padrao
            // "ControlHub" + loja vazia em vez de estourar.
            Marca = LerTextoDeConfig(config, "Marca:Nome", "ControlHub");
            Loja = LerTextoDeConfig(config, "Loja:Nome", string.Empty);
        }

        /// <summary>Rotulo do topo da planilha (`Marca:Nome`). Padrao: ControlHub.</summary>
        protected string Marca { get; }

        /// <summary>
        /// Rotulo da loja (`Loja:Nome`). Padrao: VAZIO - com a chave em branco a
        /// planilha nao ganha um titulo vazio grudado no nome da marca.
        /// </summary>
        protected string Loja { get; }

        /// <summary>
        /// Le uma chave de configuracao e devolve o padrao quando ela nao existe
        /// ou esta em branco. Centralizado porque as duas planilhas (estoque e
        /// historico) precisam do mesmo fallback.
        /// </summary>
        private static string LerTextoDeConfig(IConfiguration? config, string chave, string padrao)
        {
            var valor = config?[chave]?.Trim();
            return string.IsNullOrWhiteSpace(valor) ? padrao : valor;
        }

        // ------------------------------------------------------------------
        // BUG-12/16/17: tratamento de erro sem vazamento de informacao.
        //
        // A excecao real vai para o log do servidor (Console.Error, ja que ainda
        // nao ha ILogger configurado) e o operador recebe so uma mensagem
        // generica. Expor ex.Message entregaria nome de tabela, caminho de
        // arquivo e tipo de excecao - informacao de infraestrutura que nao
        // pertence a quem opera o caixa.
        // ------------------------------------------------------------------

        /// <summary>Mensagem devolvida ao cliente quando o erro nao e previsto.</summary>
        public const string MensagemErroGenerica = "Erro ao processar a solicitação.";

        /// <summary>
        /// Registra a excecao REAL no log do servidor. O operador ve apenas
        /// <see cref="MensagemErroGenerica"/>.
        /// </summary>
        private static void LogarExcecao(string origem, Exception ex)
        {
            Console.Error.WriteLine(
                $"[ERRO] {origem}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex}");
        }

        // Listagem Principal (Estoque)
        [Authorize(Policy = Policies.PodeOperarPDV)]
        public IActionResult Index(string busca)
        {
            // BUG-08: produtos desativados (soft delete) somem do estoque.
            var lista = _context.Produtos.Where(p => p.Ativo).ToList();

            if (!string.IsNullOrWhiteSpace(busca))
            {
                busca = busca.ToLower();
                lista = lista.Where(p => p.Nome.ToLower().Contains(busca)).ToList();
            }

            return View(lista);
        }

        [HttpGet]
        public IActionResult Configuracoes()
        {
            ViewData["Title"] = "Configurações";
            return View();
        }

        /// <summary>
        /// FASE A - ITEM 3: exporta o estoque em CSV para o dono abrir no Excel.
        ///
        /// REGRA DE OURO: a exportacao respeita o MESMO filtro de busca da Index.
        /// Se a tela mostra 3 produtos, o CSV tem exatamente esses 3 - o usuario
        /// nunca recebe uma planilha inteira pensando que exportou o que viu.
        ///
        /// NAO e dado novo, so uma visao: nao grava nada, nao calcula nada e nao
        /// mexe no caixa. Por isso nao cria migration.
        ///
        /// O EstoqueMinimo entra junto (a coluna que avisa de reposicao, do item
        /// 1) e o sinal de estoque baixo vai como SIM/NAO para o dono conseguir
        /// filtrar no Excel sem recalcular nada.
        /// </summary>
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult ExportarEstoqueCsv(string? busca)
        {
            // Mesma consulta da Index, com o mesmo filtro: um unico lugar onde
            // nasce a lista evita o "a tela mudou mas o CSV nao" de hoje.
            var lista = _context.Produtos.Where(p => p.Ativo).ToList();

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.ToLower();
                lista = lista.Where(p => p.Nome.ToLower().Contains(termo)).ToList();
            }

            var cabecalhos = new[]
            {
                "Produto", "Tipo", "Preco (R$)", "Estoque", "Estoque Minimo", "Estoque Baixo"
            };

            var linhas = lista
                // Ordem alfabetica e estavel: dois exports da mesma tela saem
                // iguais, o que ajuda quando o dono compara dois arquivos.
                .OrderBy(p => p.Nome)
                .Select(p => (IReadOnlyList<string>)new[]
                {
                    // Texto do cadastro passa pelo escudo de CSV injection.
                    ExportacaoCsv.EscaparTexto(p.Nome),
                    p.Tipo.ToString(),
                    ExportacaoCsv.Dinheiro(p.Preco),
                    ExportacaoCsv.Quantidade(p.Quantidade),
                    p.EstoqueMinimo.HasValue
                        ? ExportacaoCsv.Quantidade(p.EstoqueMinimo.Value)
                        : string.Empty,
                    p.EstoqueAbaixoDoMinimo ? "SIM" : "NAO"
                })
                .ToList();

            return ArquivoCsv(ExportacaoCsv.Montar(cabecalhos, linhas), $"estoque-{DateTime.Today:yyyy-MM-dd}.csv");
        }

        /// <summary>
        /// ETAPA A (ControlHub): a MESMA exportacao de estoque, agora em Excel
        /// (.xlsx) - ao lado do CSV, sem substitui-lo.
        ///
        /// Mesma regra de ouro e MESMA policy do CSV (PodeGerenciarEstoque): e a
        /// mesma tela, o mesmo filtro e o mesmo dado sensivel. Duas portas de
        /// saida para o mesmo dado nao podem ter niveis de acesso diferentes.
        ///
        /// O trabalho pesado (formato, formulas, cores) fica em
        /// <see cref="ExportacaoExcel"/>; aqui so mora a consulta, o filtro e o
        /// nome do arquivo - exatamente como no CSV.
        ///
        /// BUG-12/16/17: se a geracao falhar (banco fora do ar, arquivo em disco,
        /// limite de memoria), a excecao REAL vai para o log do servidor e o
        /// operador ve so uma mensagem generica - nome de tabela e caminho de
        /// arquivo nao pertencem a quem opera o caixa.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult ExportarEstoqueExcel(string? busca)
        {
            try
            {
                // Mesma consulta e mesmo filtro do CSV/Index.
                var lista = _context.Produtos.Where(p => p.Ativo).ToList();

                if (!string.IsNullOrWhiteSpace(busca))
                {
                    var termo = busca.ToLower();
                    lista = lista.Where(p => p.Nome.ToLower().Contains(termo)).ToList();
                }

                // Ordem alfabetica estavel: dois exports da mesma tela saem iguais.
                var ordenada = lista.OrderBy(p => p.Nome).ToList();

                var conteudo = ExportacaoExcel.Estoque(ordenada, Marca, Loja, busca);
                return File(conteudo, ExportacaoExcel.ContentType, $"estoque-{DateTime.Today:yyyy-MM-dd}.xlsx");
            }
            catch (Exception ex)
            {
                LogarExcecao(nameof(ExportarEstoqueExcel), ex);
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        /// <summary>
        /// Entrega os bytes como download com o nome de arquivo pedido.
        ///
        /// Centralizado aqui porque as duas exportacoes (estoque e historico)
        /// precisam do mesmo content-type e do mesmo Content-Disposition; e
        /// Content-Disposition e o que faz o navegador BAIXAR em vez de
        /// TENTAR ABRIR o csv na tela.
        /// </summary>
        private FileContentResult ArquivoCsv(byte[] conteudo, string nomeArquivo)
        {
            // "; charset=utf-8" vai junto do content-type: sem isso, o Excel
            // pode assumir latin-1 por cima do BOM.
            return File(conteudo, "text/csv; charset=utf-8", nomeArquivo);
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// BUG-22: Program.cs registra app.UseExceptionHandler("/Home/Error"), mas esta
        /// action NÃO existia. Quando uma exceção escapava em produção o handler caía no
        /// roteamento sem action e respondia 404, mascarando o erro real.
        ///
        /// Por segurança NÃO expõe stack trace nem ex.Message: devolve apenas o
        /// identificador da requisição, que permite correlacionar com o log do servidor.
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = HttpContext?.TraceIdentifier
            });
        }

        /// <summary>
        /// BUG-23 (XSS armazenado): neutraliza marcacao HTML em textos gravados
        /// pelo operador. Nao e um sanitizador completo de HTML (o app nao aceita
        /// HTML do usuario); apenas remove os caracteres que abrem/fecham tag,
        /// preservando o textoutil. Complementa - e nao substitui - a correcao
        /// no JavaScript (textContent em vez de innerHTML).
        /// </summary>
        private static string? SanitizeTexto(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return texto;

            // Remove tags inteiras <...> e depois escapa o que sobrar de perigoso.
            var semTags = System.Text.RegularExpressions.Regex.Replace(texto, "<[^>]*>", string.Empty);

            return semTags
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        // Detalhes do Produto
        [Authorize(Policy = Policies.PodeOperarPDV)]
        public IActionResult Produto(int id)
        {
            // BUG-08: produto desativado (soft delete) nao e acessivel pela tela.
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto == null) return NotFound();

            // Histórico de preço (item 2 da Fase A): mais recente primeiro. Vai no
            // ViewData em vez de criar um ViewModel novo, para não mexer na
            // assinatura que o resto do sistema já consome.
            ViewData["HistoricoPrecos"] = _context.HistoricoPrecos
                .Where(h => h.ProdutoId == produto.Id)
                .OrderByDescending(h => h.DataAlteracao)
                .ThenByDescending(h => h.Id)
                .ToList();

            return View(produto);
        }

        // Exclusão Real no Banco
        /// <summary>
        /// Exclusao LOGICA (soft delete) do produto.
        ///
        /// BUG-08: antes isto fazia _context.Produtos.Remove(produto) e a FK
        /// FK_VendaItens_Produtos_ProdutoId era ON DELETE CASCADE - o SQLite apagava em
        /// cascata TODOS os VendaItens daquele produto e o historico financeiro sumia.
        /// Agora a exclusao fisica e bloqueada pelo banco (ON DELETE RESTRICT) e o produto
        /// e apenas DESATIVADO: some das listagens (filtramos por Ativo) mas continua no
        /// banco com todo o historico de vendas preservado.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult Excluir(int id)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto != null)
            {
                produto.Ativo = false;  // BUG-08 soft delete: NUNCA Remove() aqui
                _context.SaveChanges(); // Salva a exclusão no arquivo .db
            }

            return RedirectToAction("Index");
        }

        // Movimentação de Estoque (Entrada/Saída)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult Movimentar(int id, string valor, string tipo)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);

            if (produto == null) return NotFound();

            valor = valor.Replace(",", ".");
            decimal quantidade = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);

            if (tipo == "add")
                produto.Quantidade += quantidade;
            else
                produto.Quantidade -= quantidade;

            if (produto.Quantidade < 0) produto.Quantidade = 0;

            _context.SaveChanges(); // Salva a nova quantidade no arquivo .db
            return RedirectToAction("Produto", new { id = id });
        }

        [HttpGet]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult Novo()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult Novo(string nome, string tipo, string quantidade, string preco, string? codigoBarras, string? estoqueMinimo = null, string? custoAquisicao = null, string? unidadeMedida = null)
        {
            // 1. Preço: O JS envia "500" para R$ 5,00. Dividimos por 100.
            decimal.TryParse(preco, out decimal valorBruto);
            decimal precoFinal = valorBruto / 100;

            // 2. Custo de aquisição: mesmo padrao que preco (centavos).
            decimal? custoFinal = null;
            if (!string.IsNullOrWhiteSpace(custoAquisicao))
            {
                decimal.TryParse(custoAquisicao, out decimal custoBruto);
                custoFinal = custoBruto / 100;
            }

            // BUG-10: o cadastro aceitava preço NEGATIVO (o valor era só dividido por 100).
            if (precoFinal < 0m)
            {
                TempData["Erro"] = "O preço não pode ser negativo.";
                return RedirectToAction("Index");
            }

            // 2. Quantidade: O seu JS já limpa para "10.5", usamos InvariantCulture
            decimal.TryParse(quantidade?.Replace(",", "."),
                             System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture,
                             out decimal qtd);

            // BUG-11: o cadastro aceitava estoque NEGATIVO.
            if (qtd < 0m)
            {
                TempData["Erro"] = "A quantidade não pode ser negativa.";
                return RedirectToAction("Index");
            }

            // Estoque minimo (alerta de estoque baixo). Vazio ou invalido = null
            // (sem alerta). Mesmo parse e mesma trava de negativo da Quantidade.
            decimal? minimo = null;
            if (!string.IsNullOrWhiteSpace(estoqueMinimo) &&
                decimal.TryParse(estoqueMinimo.Replace(",", "."),
                                 System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out decimal m))
            {
                if (m < 0m)
                {
                    TempData["Erro"] = "O estoque mínimo não pode ser negativo.";
                    return RedirectToAction("Index");
                }

                minimo = m;
            }

            // BUG-09: o cadastro aceitava CÓDIGO DE BARRAS DUPLICADO. Com dois produtos
            // no mesmo código, BuscarPorCodigo devolve sempre o primeiro (FirstOrDefault)
            // e o PDV passa a vender o produto errado quando o operador bipa.
            codigoBarras = codigoBarras?.Trim();
            if (!string.IsNullOrWhiteSpace(codigoBarras) &&
                _context.Produtos.Any(p => p.CodigoBarras == codigoBarras && p.Ativo))
            {
                TempData["Erro"] = $"Já existe um produto cadastrado com o código de barras {codigoBarras}.";
                return RedirectToAction("Index");
            }

            // BUG-23 (XSS armazenado): o nome era gravado cru. A view Razor escapa
            // (@Model.Nome), mas o admin-caixa.js montava o nome via innerHTML, entao
            // um produto chamado "<img src=x onerror=alert(1)>" executava script no
            // navegador do operador. defense-in-depth: o cadastro agora neutraliza as
            // tags, E o JS passou a usar textContent (commit do item 1).
            var nomeLimpo = SanitizeTexto(nome);

            var p = new Produto
            {
                Nome = nomeLimpo?.ToUpper() ?? string.Empty,
                // Enum.TryParse sozinho aceita "99" e "-5": sao numeros, e o Parse
                // devolve um valor que NAO existe em TipoProduto. O produto ficava
                // gravado com Tipo invalido e o PDV (que compara com PorPeso) e a
                // migration que filtra Tipo = 0 deixavam de enxergar a linha.
                // IsDefined fecha isso: fora do enum, cai no fallback.
                Tipo = Enum.TryParse(tipo, out TipoProduto t) && Enum.IsDefined(typeof(TipoProduto), t)
                    ? t
                    : TipoProduto.Prateleira,
                Quantidade = qtd,
                Preco = precoFinal,
                CustoAquisicao = (custoFinal ?? 0m),
                CodigoBarras = codigoBarras,
                EstoqueMinimo = minimo
            };

            _context.Produtos.Add(p);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }
        [HttpGet]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult Editar(int id)
        {
            // Procura o produto pelo ID
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);

            // Se não achar, avisa que não existe
            if (produto == null) return NotFound();

            // Abre a página "Editar.cshtml" levando os dados do produto junto
            return View(produto);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public IActionResult Editar(int id, string nome, string tipo, string? quantidade, string preco, string? estoqueMinimo = null, string? custoAquisicao = null)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto == null) return NotFound();

            // 1. Quantidade
            // 2. Preço (IGUAL AO NOVO)
            string apenasNumeros = new string((preco ?? "0").Where(char.IsDigit).ToArray());
            decimal.TryParse(apenasNumeros, out decimal precoBruto);
            decimal precoFinal = precoBruto / 100;

            // Custo de aquisição (mesmo padrao que preco).
            decimal? custoFinal = null;
            if (!string.IsNullOrWhiteSpace(custoAquisicao))
            {
                decimal.TryParse(custoAquisicao, out decimal custoBruto);
                custoFinal = custoBruto / 100;
            }

            // Atualiza custo de aquisição no produto
            if (custoFinal.HasValue)
            {
                produto.CustoAquisicao = custoFinal.Value;
            }

            if (precoFinal < 0m)
            {
                TempData["Erro"] = "O preço não pode ser negativo.";
                return RedirectToAction("Index");
            }

            // BUG-23 (XSS armazenado): mesmo saneamento do cadastro (item 1).
            produto.Nome = SanitizeTexto(nome) ?? produto.Nome;

            // Preço anterior capturado ANTES de sobrescrever. A gravação do
            // histórico em si fica no fim da action (logo antes do SaveChanges),
            // DEPOIS de todas as validações: assim nenhum POST rejeitado —
            // por preço, quantidade ou estoque mínimo — consegue deixar uma
            // linha de histórico "fantasma".
            var precoAnterior = produto.Preco;

            produto.Preco = precoFinal;
            // Mesmo buraco do Novo: "99"/"-5" passavam pelo TryParse. Aqui o
            // fallback e MANTER o tipo atual - uma edicao nunca pode rebaixar um
            // produto por peso para prateleira so porque a tela mandou lixo.
            produto.Tipo = Enum.TryParse(tipo, out TipoProduto t) && Enum.IsDefined(typeof(TipoProduto), t)
                ? t
                : produto.Tipo;
            // BUG-07: Views/Home/Editar.cshtml NÃO tem o campo "quantidade". A action
            // fazia TryParse sem checar o retorno, então qtd virava 0 e a linha
            // `produto.Quantidade = qtd` ZERAVA todo o estoque a cada edição.
            // Agora Quantidade só é sobrescrita quando o campo foi de fato enviado
            // (e é um número válido). O ajuste de estoque é feito por AjustarEstoque.
            if (!string.IsNullOrWhiteSpace(quantidade) &&
                decimal.TryParse(quantidade.Replace(",", "."),
                                 System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out decimal qtd))
            {
                if (qtd < 0m)
                {
                    TempData["Erro"] = "A quantidade não pode ser negativa.";
                    return RedirectToAction("Index");
                }

                produto.Quantidade = qtd;
            }

            // Estoque minimo: mesma politica do BUG-07 acima — só sobrescreve
            // quando o campo foi enviado E é um número válido, para a edição
            // não zerar o alerta configurado.
            if (!string.IsNullOrWhiteSpace(estoqueMinimo) &&
                decimal.TryParse(estoqueMinimo.Replace(",", "."),
                                 System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out decimal m))
            {
                if (m < 0m)
                {
                    TempData["Erro"] = "O estoque mínimo não pode ser negativo.";
                    return RedirectToAction("Index");
                }

                produto.EstoqueMinimo = m;
            }

            // Histórico de PREÇO (item 2 da Fase A): log informativo para o dono
            // responder "isso antes custava quanto?". Fica AQUI, depois de todas
            // as validações e imediatamente antes do SaveChanges, para que um
            // POST rejeitado por qualquer validação acima nunca produza uma
            // linha órfã. E só grava quando o valor muda DE VERDADE — salvar
            // sem mexer no preço não suja o histórico.
            if (precoAnterior != precoFinal)
            {
                _context.HistoricoPrecos.Add(new HistoricoPreco
                {
                    ProdutoId = produto.Id,
                    PrecoAnterior = precoAnterior,
                    PrecoNovo = precoFinal,
                    DataAlteracao = DateTime.Now
                });
            }

            _context.SaveChanges();
            return RedirectToAction("Index");
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public async Task<IActionResult> AjustarEstoque(int id, decimal quantidadeAjuste, string operacao)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto == null) return NotFound();

            if (operacao == "adicionar")
            {
                produto.Quantidade += quantidadeAjuste;
            }
            else if (operacao == "remover")
            {
                if (produto.Quantidade >= quantidadeAjuste)
                    produto.Quantidade -= quantidadeAjuste;
                else
                    TempData["Erro"] = "Estoque insuficiente!";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Produto", new { id = id });
        }

        /// <summary>
        /// Repõe (soma) quantidade ao estoque do produto.
        /// Recebe a quantidade como STRING (o servidor faz o parse, independente da cultura).
        /// Retorna JSON com success, novoEstoque e unidade.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.PodeGerenciarEstoque)]
        public async Task<IActionResult> Repor(int id, string quantidade)
        {
            try
            {
                // 1. Normalizar entrada: trim + trocar vírgula por ponto
                var qStr = quantidade?.Trim().Replace(",", ".") ?? string.Empty;

                // 1a. String vazia -> mensagem de "maior que zero" (conforme testes)
                if (string.IsNullOrEmpty(qStr))
                {
                    return Json(new { success = false, mensagem = "Informe uma quantidade maior que zero." });
                }

                // 2. Parse estrito: apenas ponto decimal, sem separador de milhar, sem notação científica
                // AllowLeadingSign para aceitar "-" no início (ex: "-1")
                if (!decimal.TryParse(qStr, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal qtd))
                {
                    return Json(new { success = false, mensagem = "Quantidade inválida. Use apenas números." });
                }

                // 3. Maior que zero (inclui "-" que vira 0 ou falha no parse, mas aqui só chega se parse ok)
                if (qtd <= 0m)
                {
                    return Json(new { success = false, mensagem = "Informe uma quantidade maior que zero." });
                }

                // 4. Máximo 1.000.000
                if (qtd > 1_000_000m)
                {
                    return Json(new { success = false, mensagem = "A quantidade máxima por reposição é 1.000.000." });
                }

                // 5. No máximo 3 casas decimais (sem arredondar)
                if (decimal.Round(qtd, 3) != qtd)
                {
                    return Json(new { success = false, mensagem = "Use no máximo 3 casas decimais." });
                }

                // 6. Buscar produto ativo
                var produto = await _context.Produtos.FirstOrDefaultAsync<Produto>(p => p.Id == id && p.Ativo);
                if (produto == null)
                {
                    return Json(new { success = false, mensagem = "Produto não encontrado." });
                }

                // 7. Se não for por peso, exigir inteiro
                if (produto.Tipo != TipoProduto.PorPeso && qtd != Math.Truncate(qtd))
                {
                    return Json(new { success = false, mensagem = "Este produto é vendido por unidade: informe um número inteiro." });
                }

                // 8. Releitura no momento da escrita + soma em decimal + único SaveChangesAsync
                var produtoDb = await _context.Produtos.FirstAsync<Produto>(p => p.Id == id && p.Ativo);
                var saldoAnterior = produtoDb.Quantidade;
                produtoDb.Quantidade += qtd;

                var mov = new MovimentacaoEstoque
                {
                    ProdutoId = produtoDb.Id,
                    Tipo = TipoMovimentacaoEstoque.Ajuste,
                    QuantidadeMovimentada = qtd,
                    SaldoAnterior = saldoAnterior,
                    SaldoPosterior = produtoDb.Quantidade,
                    DataHora = DateTime.Now,
                    UsuarioId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    OrigemTipo = "Ajuste",
                    OrigemId = null,
                    ChaveCorrelacao = $"ajuste:{Guid.NewGuid():D}"
                };
                _context.MovimentacoesEstoque.Add(mov);

                // Auditoria do ajuste
                var formatarValor = new Func<decimal, string>(v => 
                    v % 1 == 0 ? v.ToString("N0") : v.ToString("N3").TrimEnd('0').TrimEnd(','));
                var qtdFormatada = formatarValor(qtd);
                var auditoria = AuditoriaComercial.Criar(
                    acao: AcoesAuditoria.AjusteEstoque,
                    entidade: "Produto",
                    entidadeId: produtoDb.Id.ToString(),
                    usuarioId: User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    valorAnterior: formatarValor(saldoAnterior),
                    valorNovo: formatarValor(produtoDb.Quantidade),
                    motivo: $"Ajuste de estoque: +{qtdFormatada}",
                    entidadeNome: produtoDb.Nome
                );
                _context.RegistrosAuditoria.Add(auditoria);

                await _context.SaveChangesAsync();

                var unidade = produtoDb.Tipo == TipoProduto.PorPeso ? "kg" : "un";

                return Json(new
                {
                    success = true,
                    mensagem = string.Empty,
                    novoEstoque = produtoDb.Quantidade,
                    unidade = unidade
                });
            }
            catch (Exception ex)
            {
                LogarExcecao(nameof(Repor), ex);
                return Json(new { success = false, mensagem = "Não foi possível repor o estoque. Tente novamente." });
            }
        }
    }
}
