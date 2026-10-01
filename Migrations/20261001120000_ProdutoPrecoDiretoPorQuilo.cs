using ControleEstoqueLoja.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001120000_ProdutoPrecoDiretoPorQuilo")]
public partial class ProdutoPrecoDiretoPorQuilo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "ef_guard_preco_peso" (
                "invalid_rows" INTEGER NOT NULL CHECK ("invalid_rows" = 0)
            );

            INSERT INTO "ef_guard_preco_peso" ("invalid_rows")
            SELECT COUNT(*)
            FROM "Produtos"
            WHERE "Tipo" = 0
              AND (typeof("Preco") <> 'integer'
                   OR "Preco" < 0
                   OR "Preco" > 922337203685477580);

            DROP TABLE "ef_guard_preco_peso";

            UPDATE "Produtos"
            SET "Preco" = "Preco" * 10
            WHERE "Tipo" = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Esta conversão não pode ser revertida com segurança após cadastrar preços em R$/kg. " +
            "Para restaurar a unidade anterior, recupere o backup anterior à migration.");
    }
}