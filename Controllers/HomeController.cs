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
        // Injeção do Banco de Dados
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Listagem Principal (Estoque)
        public IActionResult Index(string busca)
        {
            var lista = _context.Produtos.ToList();

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

        // Detalhes do Produto
        public IActionResult Produto(int id)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id);
            if (produto == null) return NotFound();
            return View(produto);
        }

        // Exclusão Real no Banco
        [HttpPost]
        public IActionResult Excluir(int id)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id);
            if (produto != null)
            {
                _context.Produtos.Remove(produto);
                _context.SaveChanges(); // Salva a exclusão no arquivo .db
            }

            return RedirectToAction("Index");
        }

        // Movimentação de Estoque (Entrada/Saída)
        [HttpPost]
        public IActionResult Movimentar(int id, string valor, string tipo)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id);

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
            // 1. Preço: O JS envia "500" para R$ 5,00. Dividimos por 100.
            decimal.TryParse(preco, out decimal valorBruto);
            decimal precoFinal = valorBruto / 100;

            // 2. Quantidade: O seu JS já limpa para "10.5", usamos InvariantCulture
            decimal.TryParse(quantidade?.Replace(",", "."),
                             System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture,
                             out decimal qtd);

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
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id);

            // Se não achar, avisa que não existe
            if (produto == null) return NotFound();

            // Abre a página "Editar.cshtml" levando os dados do produto junto
            return View(produto);
        }
        [HttpPost]
        public IActionResult Editar(int id, string nome, string tipo, string quantidade, string preco)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id);
            if (produto == null) return NotFound();

            // 1. Quantidade
            decimal.TryParse(quantidade?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal qtd);

            // 2. Preço (IGUAL AO NOVO)
            string apenasNumeros = new string((preco ?? "0").Where(char.IsDigit).ToArray());
            decimal.TryParse(apenasNumeros, out decimal precoBruto);
            decimal precoFinal = precoBruto / 100;

            produto.Nome = nome;
            produto.Preco = precoFinal;
            produto.Tipo = Enum.TryParse(tipo, out TipoProduto t) ? t : produto.Tipo;
            produto.Quantidade = qtd;

            _context.SaveChanges();
            return RedirectToAction("Index");
        }
        [HttpPost]
        public async Task<IActionResult> AjustarEstoque(int id, decimal quantidadeAjuste, string operacao)
        {
            var produto = await _context.Produtos.FindAsync(id);
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