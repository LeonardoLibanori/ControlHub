using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja.Data; // Ajuste para o seu namespace de dados
using ControleEstoqueLoja.Models;

namespace ControleEstoqueLoja.Controllers
{
    public class FluxoCaixaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FluxoCaixaController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var hoje = DateTime.Today;

            // Busca Vendas, Sangrias e Suprimentos de hoje
            var vendas = await _context.Vendas
                .Where(v => v.DataVenda.Date == hoje).ToListAsync();

            var operacoes = await _context.OperacoesCaixa
                .Where(o => o.DataOperacao.Date == hoje).ToListAsync();

            // Cálculos para o resumo
            ViewBag.TotalVendas = vendas.Sum(v => v.TotalVenda);
            ViewBag.TotalSuprimento = operacoes.Where(o => o.Tipo == "Suprimento").Sum(o => o.Valor);
            ViewBag.TotalSangria = operacoes.Where(o => o.Tipo == "Sangria").Sum(o => o.Valor);
            ViewBag.SaldoFinal = (ViewBag.TotalVendas + ViewBag.TotalSuprimento) - ViewBag.TotalSangria;

            return View(operacoes);
        }
    }
}