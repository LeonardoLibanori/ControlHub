using System;
using System.Globalization;

namespace ControleEstoqueLoja.Data
{
    /// <summary>
    /// Formatador estático pt-BR para texto visível da UI (valores, percentuais, quantidades, moeda).
    /// Garante CultureInfo("pt-BR") explícita independente da cultura do servidor/thread.
    /// InvariantCulture fica EXCLUSIVAMENTE para atributos internos de SVG (d, x, y, r, dasharray).
    /// </summary>
    public static class FormatoPtBr
    {
        private static readonly CultureInfo CulturaPtBr = CultureInfo.GetCultureInfo("pt-BR");

        /// <summary>
        /// Formata percentual com sinal explícito quando positivo/zero: "+0,2%", "-5,3%", "0,0%".
        /// Se nulo, retorna string vazia ou traço dependendo do uso.
        /// </summary>
        public static string Percentual(decimal? v)
        {
            if (!v.HasValue) return "—";
            var sinal = v.Value > 0m ? "+" : (v.Value < 0m ? "" : ""); // decimal format já coloca - quando negativo
            if (v.Value > 0m)
                return "+" + v.Value.ToString("F1", CulturaPtBr) + "%";
            return v.Value.ToString("F1", CulturaPtBr) + "%";
        }

        /// <summary>
        /// Formata percentual simples sem sinal positivo: "100,0%", "33,3%".
        /// </summary>
        public static string PercentualSimples(decimal v)
        {
            return v.ToString("F1", CulturaPtBr) + "%";
        }

        /// <summary>
        /// Formata moeda em Real com símbolo: "R$ 10,00".
        /// </summary>
        public static string Moeda(decimal valor)
        {
            return valor.ToString("C", CulturaPtBr);
        }

        /// <summary>
        /// Formata quantidade (até 3 casas decimais, sem zeros sobrando): "0,5", "10", "1,25".
        /// </summary>
        public static string Quantidade(decimal valor)
        {
            return valor.ToString("0.###", CulturaPtBr);
        }
    }
}

