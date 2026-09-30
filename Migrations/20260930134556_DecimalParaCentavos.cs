using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class DecimalParaCentavos : Migration
    {
        // ---------------------------------------------------------------------------
        // BUG-20. As 7 colunas monetarias deixam de ser TEXT e passam a INTEGER de
        // centavos (ver Data/ConversorCentavos.cs). O modelo ja esta no snapshot; este
        // arquivo contem apenas o SQL do rebuild, porque o SQLite NAO tem ALTER COLUMN.
        //
        // POR QUE NAO O SQL QUE O EF GERA SOZINHO (mediido, nao suposicao):
        // o AlterColumn do provider SQLite faz CREATE TABLE nova + INSERT ... SELECT cru.
        // Inserir o texto '9.99' numa coluna declarada INTEGER nao multiplica nada por
        // 100: o SQLite so aplica a afinidade numerica da coluna e grava REAL 9.99
        // (typeof='real'). Reproduzido no replay do bloco do EF: MEL continuou 9.99 e a
        // aplicacao, que divide por 100 no conversor, passou a mostrar R$ 0,0999. As
        // colunas tambem ficam fora da afinidade prometida ('1,50' permaneceria TEXT).
        // O rebuild abaixo e identico ao do EF em estrutura (tabelas, colunas, PK, FK,
        // indices, ordem) e muda somente a lista de colunas do INSERT, que converte.
        //
        // POR QUE 'PRAGMA foreign_keys = 0' PRECISA VIR FORA DA TRANSACAO:
        // o pragma e ignorado dentro de transacao (medido: continua 1). Com enforcement
        // ligado, o DROP TABLE em Vendas dispara ON DELETE CASCADE em VendaItens e apaga
        // o historico de vendas inteiro - 3 itens viraram 0 na matriz de teste, com
        // PRAGMA foreign_key_check dizendo 'ok' porque as linhas nao existem mais. E a
        // mesma familia do BUG-08. Por isso os dois pragmas abaixo usam
        // suppressTransaction: true (executam fora, o DDL continua atomico dentro).
        // ---------------------------------------------------------------------------

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
            migrationBuilder.Sql(UpSql);
            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
            migrationBuilder.Sql(DownSql);
            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }


        // ---------------------------------------------------------------------------
        // UP. Estrutura = a mesma do script do EF (tabelas ef_temp_, mesmas colunas na
        // mesma ordem, mesmas PK/FK/indices, mesma ordem de DROP/RENAME). Unica
        // diferenca: as 7 colunas monetarias passam pela expressao de conversao, que o
        // EF nao emite. A expressao e a mesma nos 7 pontos e nao usa float: o REAL
        // intermediario existe so porque o dado historico esta em texto; o ROUND cobre o
        // artefato binario (medido: '8.345' * 100 = 834.5000000000001, truncaria em 834).
        // REPLACE trata o texto com virgula que alguem digitou direto no banco; TRIM o
        // espaco; NULLIF('') + NOT NULL fazem texto vazio derrubar a migration em vez de
        // virar R$ 0,00 em silencio.
        // ---------------------------------------------------------------------------
        static readonly string UpSql = """
            CREATE TABLE "ef_temp_Vendas" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Vendas" PRIMARY KEY AUTOINCREMENT,
                "CPFCliente" TEXT NULL,
                "CodigoVendedor" TEXT NULL,
                "DataVenda" TEXT NOT NULL,
                "IsFechado" INTEGER NOT NULL,
                "MetodoPagamento" TEXT NOT NULL,
                "TotalVenda" INTEGER NOT NULL,
                "ValorRecebido" INTEGER NOT NULL DEFAULT 0,
                "ValorTroco" INTEGER NOT NULL DEFAULT 0
            );

            INSERT INTO "ef_temp_Vendas" ("Id", "CPFCliente", "CodigoVendedor", "DataVenda", "IsFechado", "MetodoPagamento", "TotalVenda", "ValorRecebido", "ValorTroco")
            SELECT "Id", "CPFCliente", "CodigoVendedor", "DataVenda", "IsFechado", "MetodoPagamento",
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("TotalVenda", ',', '.')), '') AS REAL) * 100) AS INTEGER),
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("ValorRecebido", ',', '.')), '') AS REAL) * 100) AS INTEGER),
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("ValorTroco", ',', '.')), '') AS REAL) * 100) AS INTEGER)
            FROM "Vendas";

            CREATE TABLE "ef_temp_VendaItens" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_VendaItens" PRIMARY KEY AUTOINCREMENT,
                "PrecoUnitario" INTEGER NOT NULL,
                "ProdutoId" INTEGER NOT NULL,
                "Quantidade" TEXT NOT NULL,
                "VendaId" INTEGER NOT NULL,
                CONSTRAINT "FK_VendaItens_Produtos_ProdutoId" FOREIGN KEY ("ProdutoId") REFERENCES "Produtos" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_VendaItens_Vendas_VendaId" FOREIGN KEY ("VendaId") REFERENCES "Vendas" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "ef_temp_VendaItens" ("Id", "PrecoUnitario", "ProdutoId", "Quantidade", "VendaId")
            SELECT "Id",
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("PrecoUnitario", ',', '.')), '') AS REAL) * 100) AS INTEGER),
                   "ProdutoId", "Quantidade", "VendaId"
            FROM "VendaItens";

            CREATE TABLE "ef_temp_Produtos" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Produtos" PRIMARY KEY AUTOINCREMENT,
                "Ativo" INTEGER NOT NULL,
                "CodigoBarras" TEXT NULL,
                "Nome" TEXT NOT NULL,
                "Preco" INTEGER NOT NULL,
                "Quantidade" TEXT NOT NULL,
                "Tipo" INTEGER NOT NULL
            );

            INSERT INTO "ef_temp_Produtos" ("Id", "Ativo", "CodigoBarras", "Nome", "Preco", "Quantidade", "Tipo")
            SELECT "Id", "Ativo", "CodigoBarras", "Nome",
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("Preco", ',', '.')), '') AS REAL) * 100) AS INTEGER),
                   "Quantidade", "Tipo"
            FROM "Produtos";

            CREATE TABLE "ef_temp_FluxoCaixa" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FluxoCaixa" PRIMARY KEY AUTOINCREMENT,
                "Data" TEXT NOT NULL,
                "Observacao" TEXT NULL,
                "Tipo" TEXT NOT NULL,
                "Valor" INTEGER NOT NULL
            );

            INSERT INTO "ef_temp_FluxoCaixa" ("Id", "Data", "Observacao", "Tipo", "Valor")
            SELECT "Id", "Data", "Observacao", "Tipo",
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("Valor", ',', '.')), '') AS REAL) * 100) AS INTEGER)
            FROM "FluxoCaixa";

            CREATE TABLE "ef_temp_CaixaMovimentacoes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CaixaMovimentacoes" PRIMARY KEY AUTOINCREMENT,
                "Data" TEXT NOT NULL,
                "Descricao" TEXT NOT NULL,
                "IsFechado" INTEGER NOT NULL,
                "Tipo" TEXT NOT NULL,
                "Valor" INTEGER NOT NULL
            );

            INSERT INTO "ef_temp_CaixaMovimentacoes" ("Id", "Data", "Descricao", "IsFechado", "Tipo", "Valor")
            SELECT "Id", "Data", "Descricao", "IsFechado", "Tipo",
                   CAST(ROUND(CAST(NULLIF(TRIM(REPLACE("Valor", ',', '.')), '') AS REAL) * 100) AS INTEGER)
            FROM "CaixaMovimentacoes";

            DROP TABLE "Vendas";
            ALTER TABLE "ef_temp_Vendas" RENAME TO "Vendas";

            DROP TABLE "VendaItens";
            ALTER TABLE "ef_temp_VendaItens" RENAME TO "VendaItens";

            DROP TABLE "Produtos";
            ALTER TABLE "ef_temp_Produtos" RENAME TO "Produtos";

            DROP TABLE "FluxoCaixa";
            ALTER TABLE "ef_temp_FluxoCaixa" RENAME TO "FluxoCaixa";

            DROP TABLE "CaixaMovimentacoes";
            ALTER TABLE "ef_temp_CaixaMovimentacoes" RENAME TO "CaixaMovimentacoes";

            CREATE INDEX "IX_VendaItens_ProdutoId" ON "VendaItens" ("ProdutoId");
            CREATE INDEX "IX_VendaItens_VendaId" ON "VendaItens" ("VendaId");

            -- Guarda de integridade: se sobrar qualquer FK orfa, o COUNT vem maior que
            -- zero, o CHECK estoura, e como tudo isto roda dentro da transacao da
            -- migration o ROLLBACK devolve o banco inteiro ao estado anterior. Sem ela,
            -- um DROP/RENAME mal-sucedido no meio do caminho deixaria o schema pela
            -- metade - e o PRAGMA foreign_key_check nao protege de DROP em cascata,
            -- porque linha apagada nao gera violacao: gera historia perdida.
            CREATE TABLE "ef_guarda_fk" ("violacoes" INTEGER NOT NULL CHECK ("violacoes" = 0));
            INSERT INTO "ef_guarda_fk" SELECT COUNT(*) FROM pragma_foreign_key_check();
            DROP TABLE "ef_guarda_fk";
            """;

        // ---------------------------------------------------------------------------
        // DOWN. Volta o schema TEXT com as mesmas DEFAULTS de antes ('0.0' em
        // ValorRecebido/ValorTroco, que o Down() gerado pelo EF perdia) e devolve o texto
        // no formato exato que o provider Sqlite escrevia: '9.99', '6.5', '100.0', '0.0'
        // - validado contra as 60+ strings reais medidas nos bancos.
        // Perda conhecida e documentada: um valor historico com 3a casa ('8.345') subiu
        // como 835 centavos no Up e desce como '8.35'; nao ha como recuperar a 3a casa a
        // partir de centavos. Valores com ate 2 casas voltam byte a byte iguais.
        // ---------------------------------------------------------------------------
        static readonly string DownSql = """
            CREATE TABLE "ef_temp_Vendas" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Vendas" PRIMARY KEY AUTOINCREMENT,
                "CPFCliente" TEXT NULL,
                "CodigoVendedor" TEXT NULL,
                "DataVenda" TEXT NOT NULL,
                "IsFechado" INTEGER NOT NULL,
                "MetodoPagamento" TEXT NOT NULL,
                "TotalVenda" TEXT NOT NULL,
                "ValorRecebido" TEXT NOT NULL DEFAULT '0.0',
                "ValorTroco" TEXT NOT NULL DEFAULT '0.0'
            );

            INSERT INTO "ef_temp_Vendas" ("Id", "CPFCliente", "CodigoVendedor", "DataVenda", "IsFechado", "MetodoPagamento", "TotalVenda", "ValorRecebido", "ValorTroco")
            SELECT "Id", "CPFCliente", "CodigoVendedor", "DataVenda", "IsFechado", "MetodoPagamento",
                   CASE WHEN "TotalVenda" % 100 = 0 THEN printf('%.1f', "TotalVenda" / 100.0) WHEN "TotalVenda" % 10 = 0 THEN RTRIM(printf('%.2f', "TotalVenda" / 100.0), '0') ELSE printf('%.2f', "TotalVenda" / 100.0) END,
                   CASE WHEN "ValorRecebido" % 100 = 0 THEN printf('%.1f', "ValorRecebido" / 100.0) WHEN "ValorRecebido" % 10 = 0 THEN RTRIM(printf('%.2f', "ValorRecebido" / 100.0), '0') ELSE printf('%.2f', "ValorRecebido" / 100.0) END,
                   CASE WHEN "ValorTroco" % 100 = 0 THEN printf('%.1f', "ValorTroco" / 100.0) WHEN "ValorTroco" % 10 = 0 THEN RTRIM(printf('%.2f', "ValorTroco" / 100.0), '0') ELSE printf('%.2f', "ValorTroco" / 100.0) END
            FROM "Vendas";

            CREATE TABLE "ef_temp_VendaItens" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_VendaItens" PRIMARY KEY AUTOINCREMENT,
                "PrecoUnitario" TEXT NOT NULL,
                "ProdutoId" INTEGER NOT NULL,
                "Quantidade" TEXT NOT NULL,
                "VendaId" INTEGER NOT NULL,
                CONSTRAINT "FK_VendaItens_Produtos_ProdutoId" FOREIGN KEY ("ProdutoId") REFERENCES "Produtos" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_VendaItens_Vendas_VendaId" FOREIGN KEY ("VendaId") REFERENCES "Vendas" ("Id") ON DELETE CASCADE
            );

            INSERT INTO "ef_temp_VendaItens" ("Id", "PrecoUnitario", "ProdutoId", "Quantidade", "VendaId")
            SELECT "Id",
                   CASE WHEN "PrecoUnitario" % 100 = 0 THEN printf('%.1f', "PrecoUnitario" / 100.0) WHEN "PrecoUnitario" % 10 = 0 THEN RTRIM(printf('%.2f', "PrecoUnitario" / 100.0), '0') ELSE printf('%.2f', "PrecoUnitario" / 100.0) END,
                   "ProdutoId", "Quantidade", "VendaId"
            FROM "VendaItens";

            CREATE TABLE "ef_temp_Produtos" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Produtos" PRIMARY KEY AUTOINCREMENT,
                "Ativo" INTEGER NOT NULL,
                "CodigoBarras" TEXT NULL,
                "Nome" TEXT NOT NULL,
                "Preco" TEXT NOT NULL,
                "Quantidade" TEXT NOT NULL,
                "Tipo" INTEGER NOT NULL
            );

            INSERT INTO "ef_temp_Produtos" ("Id", "Ativo", "CodigoBarras", "Nome", "Preco", "Quantidade", "Tipo")
            SELECT "Id", "Ativo", "CodigoBarras", "Nome",
                   CASE WHEN "Preco" % 100 = 0 THEN printf('%.1f', "Preco" / 100.0) WHEN "Preco" % 10 = 0 THEN RTRIM(printf('%.2f', "Preco" / 100.0), '0') ELSE printf('%.2f', "Preco" / 100.0) END,
                   "Quantidade", "Tipo"
            FROM "Produtos";

            CREATE TABLE "ef_temp_FluxoCaixa" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FluxoCaixa" PRIMARY KEY AUTOINCREMENT,
                "Data" TEXT NOT NULL,
                "Observacao" TEXT NULL,
                "Tipo" TEXT NOT NULL,
                "Valor" TEXT NOT NULL
            );

            INSERT INTO "ef_temp_FluxoCaixa" ("Id", "Data", "Observacao", "Tipo", "Valor")
            SELECT "Id", "Data", "Observacao", "Tipo",
                   CASE WHEN "Valor" % 100 = 0 THEN printf('%.1f', "Valor" / 100.0) WHEN "Valor" % 10 = 0 THEN RTRIM(printf('%.2f', "Valor" / 100.0), '0') ELSE printf('%.2f', "Valor" / 100.0) END
            FROM "FluxoCaixa";

            CREATE TABLE "ef_temp_CaixaMovimentacoes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CaixaMovimentacoes" PRIMARY KEY AUTOINCREMENT,
                "Data" TEXT NOT NULL,
                "Descricao" TEXT NOT NULL,
                "IsFechado" INTEGER NOT NULL,
                "Tipo" TEXT NOT NULL,
                "Valor" TEXT NOT NULL
            );

            INSERT INTO "ef_temp_CaixaMovimentacoes" ("Id", "Data", "Descricao", "IsFechado", "Tipo", "Valor")
            SELECT "Id", "Data", "Descricao", "IsFechado", "Tipo",
                   CASE WHEN "Valor" % 100 = 0 THEN printf('%.1f', "Valor" / 100.0) WHEN "Valor" % 10 = 0 THEN RTRIM(printf('%.2f', "Valor" / 100.0), '0') ELSE printf('%.2f', "Valor" / 100.0) END
            FROM "CaixaMovimentacoes";

            DROP TABLE "Vendas";
            ALTER TABLE "ef_temp_Vendas" RENAME TO "Vendas";

            DROP TABLE "VendaItens";
            ALTER TABLE "ef_temp_VendaItens" RENAME TO "VendaItens";

            DROP TABLE "Produtos";
            ALTER TABLE "ef_temp_Produtos" RENAME TO "Produtos";

            DROP TABLE "FluxoCaixa";
            ALTER TABLE "ef_temp_FluxoCaixa" RENAME TO "FluxoCaixa";

            DROP TABLE "CaixaMovimentacoes";
            ALTER TABLE "ef_temp_CaixaMovimentacoes" RENAME TO "CaixaMovimentacoes";

            CREATE INDEX "IX_VendaItens_ProdutoId" ON "VendaItens" ("ProdutoId");
            CREATE INDEX "IX_VendaItens_VendaId" ON "VendaItens" ("VendaId");

            CREATE TABLE "ef_guarda_fk" ("violacoes" INTEGER NOT NULL CHECK ("violacoes" = 0));
            INSERT INTO "ef_guarda_fk" SELECT COUNT(*) FROM pragma_foreign_key_check();
            DROP TABLE "ef_guarda_fk";
            """;

    }
}
