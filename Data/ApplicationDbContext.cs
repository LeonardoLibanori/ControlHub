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
        public DbSet<HistoricoPreco> HistoricoPrecos { get; set; }
        public DbSet<MovimentacaoEstoque> MovimentacoesEstoque { get; set; }
        public DbSet<RegistroAuditoria> RegistrosAuditoria { get; set; }

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
            // (Produto e VendaItem) fica de FORA de proposito: quantidade por peso
            // tem 3+ casas (1,235 kg) e nao e dinheiro; converter centavo nao vale
            // para quilo, e a correcao nao pode quebrar pesagem. Ela permanece TEXT
            // e segue como divida separada no relatorio de QA.
            //
            // O padrao de registro e HasConversion em vez de atributo na classe para
            // deixar explicito no unico lugar que descreve o banco quais campos sao
            // monetarios - a nova regra de arquitetura da suite de QA audita exatamente
            // esta lista.
            // ------------------------------------------------------------------
            var centavos = ConversorCentavos.ParaBanco;

            modelBuilder.Entity<Produto>().Property(p => p.Preco).HasConversion(centavos);

            // Custo de aquisição: mesmo padrao que preco (centavos).
            modelBuilder.Entity<Produto>().Property(p => p.CustoAquisicao).HasConversion(centavos);

            modelBuilder.Entity<Venda>().Property(v => v.TotalVenda).HasConversion(centavos);
            modelBuilder.Entity<Venda>().Property(v => v.ValorRecebido).HasConversion(centavos).HasDefaultValue(0m);
            modelBuilder.Entity<Venda>().Property(v => v.ValorTroco).HasConversion(centavos).HasDefaultValue(0m);

            modelBuilder.Entity<VendaItem>().Property(i => i.PrecoUnitario)
            .HasConversion(centavos);

                modelBuilder.Entity<VendaItem>().Property(i => i.Desconto)
                 .HasConversion(centavos)
    .HasDefaultValue(0m);

modelBuilder.Entity<VendaItem>().Property(i => i.CustoUnitario)
    .HasConversion(centavos);

            modelBuilder.Entity<CaixaMovimentacao>().Property(m => m.Valor).HasConversion(centavos);

            modelBuilder.Entity<FluxoCaixa>().Property(f => f.Valor).HasConversion(centavos);

            // Histórico de preço (item 2 da Fase A): os dois valores são dinheiro,
            // então entram na mesma lista de colunas monetárias (INTEGER de centavos).
            modelBuilder.Entity<HistoricoPreco>().Property(h => h.PrecoAnterior).HasConversion(centavos);
            modelBuilder.Entity<HistoricoPreco>().Property(h => h.PrecoNovo).HasConversion(centavos);

            // Um produto pode ter vários registros de preço. A FK segue o mesmo
            // espírito do BUG-08 (RESTRICT): apagar um produto que já teve preço
            // alterado não pode destruir o histórico — e o cadastro é sempre
            // exclusão lógica (Ativo = false), então isso é só uma rede de segurança.
            modelBuilder.Entity<HistoricoPreco>()
                .HasOne(h => h.Produto)
                .WithMany()
                .HasForeignKey(h => h.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict);

                // Idempotência de vendas: permite identificar uma tentativa lógica
// e impede que a mesma chave seja gravada em duas vendas.
modelBuilder.Entity<Venda>(entity =>
{
    entity.Property(v => v.ChaveIdempotencia)
        .HasMaxLength(36);

    entity.Property(v => v.HashConteudo)
        .HasMaxLength(64);

    entity.HasIndex(v => v.ChaveIdempotencia)
        .IsUnique()
        .HasDatabaseName("IX_Vendas_ChaveIdempotencia");
});
modelBuilder.Entity<MovimentacaoEstoque>()
    .HasOne(m => m.Produto)
    .WithMany()
    .HasForeignKey(m => m.ProdutoId)
    .OnDelete(DeleteBehavior.Restrict);

        
            // Kardex: preservar os limites e índices definidos na migration histórica.
            modelBuilder.Entity<MovimentacaoEstoque>(entity =>
            {
                entity.Property(m => m.UsuarioId).HasMaxLength(450);
                entity.Property(m => m.Motivo).HasMaxLength(200);
                entity.Property(m => m.OrigemTipo).HasMaxLength(40);
                entity.Property(m => m.ChaveCorrelacao).HasMaxLength(80);

                entity.HasIndex(m => m.ChaveCorrelacao)
                    .IsUnique()
                    .HasDatabaseName("IX_MovimentacoesEstoque_ChaveCorrelacao");

                entity.HasIndex(m => m.DataHora)
                    .HasDatabaseName("IX_MovimentacoesEstoque_DataHora");

                entity.HasIndex(m => new { m.ProdutoId, m.DataHora })
                    .HasDatabaseName("IX_MovimentacoesEstoque_ProdutoId_DataHora");

                entity.HasIndex(m => m.Tipo)
                    .HasDatabaseName("IX_MovimentacoesEstoque_Tipo");

                entity.HasOne(m => m.Produto)
                    .WithMany()
                    .HasForeignKey(m => m.ProdutoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Auditoria: preservar os limites e índices definidos historicamente.
            modelBuilder.Entity<RegistroAuditoria>(entity =>
            {
                entity.Property(a => a.UsuarioId).HasMaxLength(450);
                entity.Property(a => a.Acao).HasMaxLength(80).IsRequired();
                entity.Property(a => a.Entidade).HasMaxLength(80).IsRequired();
                entity.Property(a => a.EntidadeId).HasMaxLength(80);
                entity.Property(a => a.ValorAnterior).HasMaxLength(200);
                entity.Property(a => a.ValorNovo).HasMaxLength(200);
                entity.Property(a => a.Motivo).HasMaxLength(200);

                entity.HasIndex(a => a.Acao)
                    .HasDatabaseName("IX_RegistrosAuditoria_Acao");

                entity.HasIndex(a => a.DataHora)
                    .HasDatabaseName("IX_RegistrosAuditoria_DataHora");

                entity.HasIndex(a => new { a.Entidade, a.EntidadeId })
                    .HasDatabaseName("IX_RegistrosAuditoria_Entidade_EntidadeId");
            });

            // Custo de aquisição armazenado em centavos, com default compatível
            // com a migration histórica de Etapa 4.
            modelBuilder.Entity<Produto>()
                .Property(p => p.CustoAquisicao)
                .HasDefaultValue(0m);

        
        }
    }
}