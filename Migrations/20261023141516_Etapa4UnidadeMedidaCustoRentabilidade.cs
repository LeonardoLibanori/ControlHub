using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class Etapa4UnidadeMedidaCustoRentabilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CustoUnitario",
                table: "VendaItens",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Desconto",
                table: "VendaItens",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CustoAquisicao",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "UnidadeMedida",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill: produtos existentes mapeiam TipoProduto.PorPeso -> Quilograma (1),
            // demais (Prateleira/Geladeira/Freezer) -> Unidade (0).
            // Nao altera quantidade nem custo: apenas classifica a unidade de venda.
            migrationBuilder.Sql(
                @"UPDATE ""Produtos"" SET ""UnidadeMedida"" = 1 WHERE ""Tipo"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustoUnitario",
                table: "VendaItens");

            migrationBuilder.DropColumn(
                name: "Desconto",
                table: "VendaItens");

            migrationBuilder.DropColumn(
                name: "CustoAquisicao",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "UnidadeMedida",
                table: "Produtos");
        }
    }
}
