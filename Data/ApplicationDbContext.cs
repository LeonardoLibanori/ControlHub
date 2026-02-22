using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja.Models;

namespace ControleEstoqueLoja.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<FluxoCaixa> FluxoCaixa { get; set; }

        // ESSAS LINHAS SÃO O QUE FAZEM O CONTADOR SAIR DO 0
        public DbSet<Produto> Produtos { get; set; }
        public DbSet<Venda> Vendas { get; set; }
        public DbSet<VendaItem> VendaItens { get; set; }
        public DbSet<CaixaMovimentacao> CaixaMovimentacoes { get; set; }
        public DbSet<Vendedor> Vendedores { get; set; } // Adicione esta linha
    }
}