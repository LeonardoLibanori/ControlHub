using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class EstoqueMinimoProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstoqueMinimo",
                table: "Produtos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstoqueMinimo",
                table: "Produtos");
        }
    }
}
