using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class AddValorRecebidoValorTroco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ValorRecebido",
                table: "Vendas",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorTroco",
                table: "Vendas",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValorRecebido",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ValorTroco",
                table: "Vendas");
        }
    }
}
