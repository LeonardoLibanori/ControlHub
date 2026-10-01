using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ControleEstoqueLoja.Models;

namespace ControleEstoqueLoja.Data
{
    /// <summary>
    /// Contexto unico da aplicacao: dominio (produtos/vendas/caixa) + Identity
    /// (login). Herdar de IdentityDbContext<Usuario> traz as 7 tabelas do ASP.NET
    /// Core Identity (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.) e o
    /// UserManager/RoleManager, sem colidir com nenhuma tabela de negocio.
    /// </summary>
    public class ApplicationDbContext : IdentityDbContext<Usuario>
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // BUG-08: a migration Inicial_Nova criou a FK VendaItens.ProdutoId com
            // ON DELETE CASCADE. Ao excluir um produto, o SQLite apagava em cascata
            // TODOS os VendaItens daquele produto — o histórico de vendas (e portanto
            // o relatório financeiro) era perdido silenciosamente.
            //
            // Com RESTRICT o próprio banco recusa a exclusão física de um produto que
            // já possui itens de venda. A exclusão passa a ser LÓGICA
            // (HomeController.Excluir marca Ativo = false), preservando o histórico.
            modelBuilder.Entity<VendaItem>()
                .HasOne(vi => vi.Produto)
                .WithMany(p => p.VendaItems)
                .HasForeignKey(vi => vi.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict);

            // ------------------------------------------------------------------
            // BUG-20: dinheiro vira INTEGER de centavos no banco.
            //
            // As 7 colunas abaixo sao a superficie monetaria do sistema. Quantidade
            // (Produto e VendaItem) fica de FORA de proposito: quantidade em unidade
            // fracionavel tem 3+ casas (0,335 kg, 1,25 l) e nao e dinheiro; conversao
            // de centavo nao vale para unidade de medida e nao pode quebrar pesagem.
            // Ela permanece TEXT e segue como divida separada no
            // relatorio de QA.
            //
            // O padrao de registro e HasConversion em vez de atributo na classe para
            // deixar explicito no unico lugar que descreve o banco quais campos sao
            // monetarios - a nova regra de arquitetura da suite de QA audita exatamente
            // esta lista.
            // ------------------------------------------------------------------
            var centavos = ConversorCentavos.ParaBanco;

            modelBuilder.Entity<Produto>().Property(p => p.Preco).HasConversion(centavos);

            modelBuilder.Entity<Venda>().Property(v => v.TotalVenda).HasConversion(centavos);
            modelBuilder.Entity<Venda>().Property(v => v.ValorRecebido).HasConversion(centavos).HasDefaultValue(0m);
            modelBuilder.Entity<Venda>().Property(v => v.ValorTroco).HasConversion(centavos).HasDefaultValue(0m);

            modelBuilder.Entity<VendaItem>().Property(i => i.PrecoUnitario).HasConversion(centavos);

            modelBuilder.Entity<CaixaMovimentacao>().Property(m => m.Valor).HasConversion(centavos);

            modelBuilder.Entity<FluxoCaixa>().Property(f => f.Valor).HasConversion(centavos);

            // ------------------------------------------------------------------
            // Unidade de medida do produto. Substitui o multiplicador x10 que estava
            // preso ao valor "Granel" do enum TipoProduto: agora Preco e sempre o
            // preco de UMA Unidade, e PermiteFracionado diz se o caixa pode aceitar
            // 0,335 ou se so aceita inteiro.
            //
            // Texto livre de caso curto de proposito - "un", "kg", "l", "cx", "pct",
            // "m", "dz"... uma enum recriaria justamente a rigidez que esta mudanca
            // tira do sistema (o comercio muda, o codigo nao deveria mudar junto).
            // MaxLength e cinto de seguranca contra payload grande no cadastro, nao
            // uma lista fechada de valores.
            // ------------------------------------------------------------------
            modelBuilder.Entity<Produto>().Property(p => p.Unidade)
                .HasMaxLength(12)
                .IsRequired();
        }
    }
}