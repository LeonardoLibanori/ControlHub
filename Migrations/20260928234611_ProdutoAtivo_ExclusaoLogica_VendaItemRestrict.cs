using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class ProdutoAtivo_ExclusaoLogica_VendaItemRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VendaItens_Produtos_ProdutoId",
                table: "VendaItens");

            migrationBuilder.AddColumn<bool>(
                name: "Ativo",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                // ATENÇÃO: o EF sugeriu defaultValue: false (default do CLR para bool),
                // o que DESATIVARIA todos os produtos já cadastrados e os faria sumir
                // das listagens. Os produtos existentes devem continuar ATIVOS.
                defaultValue: true);

            migrationBuilder.AddForeignKey(
                name: "FK_VendaItens_Produtos_ProdutoId",
                table: "VendaItens",
                column: "ProdutoId",
                principalTable: "Produtos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VendaItens_Produtos_ProdutoId",
                table: "VendaItens");

            migrationBuilder.DropColumn(
                name: "Ativo",
                table: "Produtos");

            migrationBuilder.AddForeignKey(
                name: "FK_VendaItens_Produtos_ProdutoId",
                table: "VendaItens",
                column: "ProdutoId",
                principalTable: "Produtos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
