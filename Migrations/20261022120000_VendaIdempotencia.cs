using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ControleEstoqueLoja.Data;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261022120000_VendaIdempotencia")]
    public partial class VendaIdempotencia : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaveIdempotencia",
                table: "Vendas",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HashConteudo",
                table: "Vendas",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ChaveIdempotencia",
                table: "Vendas",
                column: "ChaveIdempotencia",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_ChaveIdempotencia",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ChaveIdempotencia",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "HashConteudo",
                table: "Vendas");
        }
    }
}
