using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControleEstoqueLoja.Migrations
{
    /// <inheritdoc />
    public partial class UnidadeMedidaProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) As duas colunas novas. "un" como default da coluna (e nao "") para
            //    que qualquer linha existente - ou inserida por SQL solto - signifique
            //    "un" no sentido que a loja ja usava quando nao dizia nada.
            migrationBuilder.AddColumn<bool>(
                name: "PermiteFracionado",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Unidade",
                table: "Produtos",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "un");

            // 2) Conversao das linhas que eram "Granel" (Tipo = 0, codigo do enum
            //    antigo; o enum novo comecou em Prateleira = 1 justamente para nao
            //    precisar renumerar ninguem).
            //
            //    Antes: Preco guardava o valor de 100 g e a quantidade entrava em kg,
            //    e o caixa refazia a conta multiplicando por 10 (ver o comentario
            //    historico em CalcularSubtotalItem). Agora Preco e o valor de UMA
            //    unidade, entao o preco de um produto a granel precisa ser multiplicado
            //    por 10 para que o cliente continue pagando exatamente o mesmo.
            //    O calculo e exato: Preco esta gravado em CENTAVOS (INTEGER, migration
            //    DecimalParaCentavos), entao x10 nao cria resto de arredondamento.
            //
            //    A quantidade NAO muda: ela ja estava em kg, que passa a ser a unidade
            //    declarada na linha.
            //
            //    Depois do UPDATE nenhum registro resta com Tipo = 0, o que torna o
            //    script idempotente - rodar de novo nao cobra duas vezes.
            migrationBuilder.Sql(@"
UPDATE ""Produtos""
SET ""Preco""              = ""Preco"" * 10,
    ""Tipo""               = 1,
    ""Unidade""            = 'kg',
    ""PermiteFracionado""  = 1
WHERE ""Tipo"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverte pelo caminho inverso, na medida do possivel. Limitacao honesta:
            // nao ha como distinguir o produto que era Granel do produto cadastrado
            // depois da migracao como "kg" e fracionavel - os dois viraram a mesma
            // linha. Ambos voltam para Granel e o preco volta dividido por 10 (com
            // arredondamento para o centavo mais proximo). Reverter em producao deve
            // ser decisao tomada com backup na mao, nao um Ctrl+Z.
            migrationBuilder.Sql(@"
UPDATE ""Produtos""
SET ""Preco""             = CAST(ROUND(""Preco"" / 10.0) AS INTEGER),
    ""Tipo""              = 0,
    ""Unidade""           = '',
    ""PermiteFracionado"" = 0
WHERE ""Unidade"" = 'kg' AND ""PermiteFracionado"" = 1;");

            migrationBuilder.DropColumn(
                name: "PermiteFracionado",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Unidade",
                table: "Produtos");
        }
    }
}
