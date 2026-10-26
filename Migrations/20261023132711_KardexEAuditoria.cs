using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class KardexEAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MovimentacoesEstoque",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantidadeMovimentada = table.Column<decimal>(type: "TEXT", nullable: false),
                    SaldoAnterior = table.Column<decimal>(type: "TEXT", nullable: false),
                    SaldoPosterior = table.Column<decimal>(type: "TEXT", nullable: false),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OrigemTipo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OrigemId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChaveCorrelacao = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentacoesEstoque", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentacoesEstoque_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegistrosAuditoria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Acao = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Entidade = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntidadeId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ValorAnterior = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ValorNovo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrosAuditoria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesEstoque_ChaveCorrelacao",
                table: "MovimentacoesEstoque",
                column: "ChaveCorrelacao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesEstoque_DataHora",
                table: "MovimentacoesEstoque",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesEstoque_ProdutoId_DataHora",
                table: "MovimentacoesEstoque",
                columns: new[] { "ProdutoId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentacoesEstoque_Tipo",
                table: "MovimentacoesEstoque",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_Acao",
                table: "RegistrosAuditoria",
                column: "Acao");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_DataHora",
                table: "RegistrosAuditoria",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_Entidade_EntidadeId",
                table: "RegistrosAuditoria",
                columns: new[] { "Entidade", "EntidadeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovimentacoesEstoque");

            migrationBuilder.DropTable(
                name: "RegistrosAuditoria");
        }
    }
}
