using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Globalization;
using System.Linq;

namespace ControleEstoqueLoja.Controllers
{
    public class HomeController : Controller
    {
        // Inje��o do Banco de Dados
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Listagem Principal (Estoque)
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
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = HttpContext?.TraceIdentifier
            });
        }

        // Detalhes do Produto
        public IActionResult Produto(int id)
        {
            // BUG-08: produto desativado (soft delete) nao e acessivel pela tela.
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto == null) return NotFound();
            return View(produto);
        }

        // Exclus�o Real no Banco
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
        public IActionResult Excluir(int id)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto != null)
            {
                produto.Ativo = false;  // BUG-08 soft delete: NUNCA Remove() aqui
                _context.SaveChanges(); // Salva a exclus�o no arquivo .db
            }

            return RedirectToAction("Index");
        }

        // Movimenta��o de Estoque (Entrada/Sa�da)
        [HttpPost]
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
        public IActionResult Novo()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Novo(string nome, string tipo, string quantidade, string preco, string codigoBarras)
        {
            // 1. Pre�o: O JS envia "500" para R$ 5,00. Dividimos por 100.
            decimal.TryParse(preco, out decimal valorBruto);
            decimal precoFinal = valorBruto / 100;

            // BUG-10: o cadastro aceitava preço NEGATIVO (o valor era só dividido por 100).
            if (precoFinal < 0m)
            {
                TempData["Erro"] = "O preço não pode ser negativo.";
                return RedirectToAction("Index");
            }

            // 2. Quantidade: O seu JS j� limpa para "10.5", usamos InvariantCulture
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

            var p = new Produto
            {
                Nome = nome?.ToUpper(),
                Tipo = Enum.TryParse(tipo, out TipoProduto t) ? t : TipoProduto.Prateleira,
                Quantidade = qtd,
                Preco = precoFinal,
                CodigoBarras = codigoBarras
            };

            _context.Produtos.Add(p);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }
        [HttpGet]
        public IActionResult Editar(int id)
        {
            // Procura o produto pelo ID
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);

            // Se n�o achar, avisa que n�o existe
            if (produto == null) return NotFound();

            // Abre a p�gina "Editar.cshtml" levando os dados do produto junto
            return View(produto);
        }
        [HttpPost]
        public IActionResult Editar(int id, string nome, string tipo, string quantidade, string preco)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id && p.Ativo);
            if (produto == null) return NotFound();

            // 1. Quantidade
            // 2. Preço (IGUAL AO NOVO)
            string apenasNumeros = new string((preco ?? "0").Where(char.IsDigit).ToArray());
            decimal.TryParse(apenasNumeros, out decimal precoBruto);
            decimal precoFinal = precoBruto / 100;

            if (precoFinal < 0m)
            {
                TempData["Erro"] = "O preço não pode ser negativo.";
                return RedirectToAction("Index");
            }

            produto.Nome = nome;
            produto.Preco = precoFinal;
            produto.Tipo = Enum.TryParse(tipo, out TipoProduto t) ? t : produto.Tipo;
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

            _context.SaveChanges();
            return RedirectToAction("Index");
        }
        [HttpPost]
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
    }
}