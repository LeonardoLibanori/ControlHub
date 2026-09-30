using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ControleEstoqueLoja.Data
{
    /// <summary>
    /// BUG-20: os campos monetarios eram decimal .NET persistidos como TEXT no SQLite.
    /// O SQLite nao tem sistema de tipos rigido: uma coluna TEXT armazena o texto que o
    /// provider gravou e passa a ordenar/agrupar por collation BINARIA, nao por valor
    /// numerico. Resultado medido na suíte de QA: OrderBy("9.99", "10.5", "100.0") devolve
    /// 10.5 antes de 9.99, e Min/Max/Media/GroupBy(Sum) herdam o mesmo erro.
    ///
    /// A correcao nao e mudar a declaracao da coluna (o SQLite continuaria aceitando
    /// qualquer blob la dentro); e fazer o modelo gravar UM INTEIRO DE CENTAVOS.
    /// INTEGER é a unica afinidade numerica verdadeiramente exata do SQLite: ordena por
    /// valor, soma sem acumulo de ponto flutuante e nao depende de parsing de texto.
    ///
    /// Exatamente 2 casas decimais e a natureza do dinheiro em BRL, nao uma limitacao
    /// artificial: todo o codigo que consome estas propriedades ja assume decimal(2), e
    /// notas fiscais / cupom / sangria nunca carregam fracao de centavo.
    ///
    /// Nada aqui passa por float/double: decimal * 100m e decimal / 100m sao exatos.
    /// O unico arredondamento da cadeia esta no SQL da migration (ROUND sobre o REAL
    /// intermediario), porque la o dado historico e texto e precisa virar numero.
    /// </summary>
    public static class ConversorCentavos
    {
        /// <summary>
        /// Tetos arbitrados com folga: |long.MaxValue|/100 com uma casa de margem para
        /// somatorios. Ultrapassar isso estouraria o INTEGER do SQLite (e o long do .NET)
        /// e produziria um valor corrompido em silencio - entao recusamos antes.
        /// </summary>
        public const long MaxCentavos = 92_233_720_368_547_758L; // R$ 922 trilhoes

        /// <summary>Conversor unico registrado nas 7 colunas monetarias do contexto.</summary>
        public static readonly ValueConverter<decimal, long> ParaBanco =
            new(valor => ParaCentavos(valor), centavos => ParaDecimal(centavos));

        /// <summary>
        /// decimal -&gt; centavos. Recusa (falha alto, sem arredondar) a 3a casa decimal:
        /// arredondar aqui significaria alterar um valor monetario que o operador digitou,
        /// e o criterio de sucesso da correcao e "gravar 9.99 grava 999", nao "aproxima".
        /// </summary>
        public static long ParaCentavos(decimal valor)
        {
            var centavos = valor * 100m;

            if (centavos > MaxCentavos || centavos < -MaxCentavos)
                throw new OverflowException(
                    $"Valor monetario {valor} excede a faixa armazenavel (maximo {MaxCentavos / 100m:0.00}).");

            if (decimal.Truncate(centavos) != centavos)
                throw new ArgumentOutOfRangeException(nameof(valor),
                    $"Valor monetario {valor} tem mais de 2 casas decimais; nenhum arredondamento e feito.");

            return (long)centavos;
        }

        /// <summary>centavos -&gt; decimal. Exato: divisao de long por 100m em decimal.</summary>
        public static decimal ParaDecimal(long centavos) => centavos / 100m;
    }
}
