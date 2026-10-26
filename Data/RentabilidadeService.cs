using ControleEstoqueLoja.Models;

namespace ControleEstoqueLoja.Data
{
    /// <summary>
    /// Calculos de rentabilidade: receita liquida, custo, lucro bruto e margem bruta.
    /// Fonte unica de verdade para evitar fórmulas divergentes em controllers e views.
    ///
    /// Regras implementadas:
    ///  - Receita liquida do item = (Quantidade × PrecoUnitario) − Desconto
    ///  - Custo total do item = Quantidade × CustoUnitario (snapshot historico)
    ///  - Lucro bruto do item = Receita liquida − Custo total
    ///  - Lucro bruto da venda = soma dos itens (desconto ja rateado por item)
    ///  - Margem bruta % = Lucro bruto / Receita liquida × 100, se Receita liquida > 0
    ///  - Venda sem custo historico (CustoUnitario nulo) => Rentabilidade indisponível
    ///  - Receita liquida zero => margem nao calculada (evita divisao por zero)
    ///  - Nao confunde margem com markup. Nao inclui despesas operacionais/impostos.
    /// </summary>
    public static class RentabilidadeService
    {
        /// <summary>Resultado do cálculo de rentabilidade de um único item.</summary>
        public sealed class ItemResultado
        {
            public decimal Quantidade { get; init; }
            public decimal PrecoUnitario { get; init; }
            public decimal Desconto { get; init; }
            public decimal SubtotalBruto { get; init; }
            public decimal ReceitaLiquida { get; init; }
            public decimal? CustoUnitario { get; init; }
            public decimal? CustoTotal { get; init; }
            public decimal? LucroBruto { get; init; }
            public decimal? MargemBrutaPercentual { get; init; }
            public bool CustoDisponivel => CustoUnitario.HasValue;
        }

        /// <summary>Resultado do cálculo de rentabilidade de uma venda completa.</summary>
        public sealed class VendaResultado
        {
            public int VendaId { get; init; }
            public decimal ReceitaLiquidaTotal { get; init; }
            public decimal CustoTotal { get; init; }
            public decimal LucroBrutoTotal { get; init; }
            public decimal? MargemBrutaPercentual { get; init; }
            public bool TemCustoParaTodosOsItens { get; init; }
            public List<ItemResultado> Itens { get; init; } = new();
        }

        /// <summary>
        /// Calcula a rentabilidade de um item de venda individual.
        /// Se CustoUnitario for nulo, custo/lucro/margem saem como nulos (indisponível).
        /// </summary>
        public static ItemResultado CalcularItem(
            decimal quantidade,
            decimal precoUnitario,
            decimal desconto,
            decimal? custoUnitario)
        {
            if (quantidade < 0m) throw new ArgumentOutOfRangeException(nameof(quantidade), "Quantidade não pode ser negativa.");
            if (precoUnitario < 0m) throw new ArgumentOutOfRangeException(nameof(precoUnitario), "Preço unitário não pode ser negativo.");
            if (desconto < 0m) throw new ArgumentOutOfRangeException(nameof(desconto), "Desconto não pode ser negativo.");

            var subtotalBruto = quantidade * precoUnitario;
            var receitaLiquida = subtotalBruto - desconto;
            if (receitaLiquida < 0m) receitaLiquida = 0m; // nunca negativa por defesa

            decimal? custoTotal = null;
            decimal? lucroBruto = null;
            decimal? margemBrutaPercentual = null;

            if (custoUnitario.HasValue)
            {
                custoTotal = quantidade * custoUnitario.Value;
                lucroBruto = receitaLiquida - custoTotal.Value;

                if (receitaLiquida > 0m)
                {
                    margemBrutaPercentual = Math.Round(
                        lucroBruto.Value / receitaLiquida * 100m,
                        2,
                        MidpointRounding.AwayFromZero);
                }
                // Receita líquida zero mantém margem nula para evitar divisão por zero.
            }

            return new ItemResultado
            {
                Quantidade = quantidade,
                PrecoUnitario = precoUnitario,
                Desconto = desconto,
                SubtotalBruto = subtotalBruto,
                ReceitaLiquida = receitaLiquida,
                CustoUnitario = custoUnitario,
                CustoTotal = custoTotal,
                LucroBruto = lucroBruto,
                MargemBrutaPercentual = margemBrutaPercentual
            };
        }

        /// <summary>
        /// Calcula a rentabilidade de uma venda completa a partir de seus itens.
        /// O desconto ja esta rateado por item (Desconto em cada VendaItem),
        /// entao nao há dupla contabilização.
        /// </summary>
        public static VendaResultado CalcularVenda(int vendaId, IEnumerable<VendaItem> itens)
        {
            var listaItens = new List<ItemResultado>();
            decimal receitaLiquidaTotal = 0m;
            decimal custoTotal = 0m;
            bool temCustoParaTodos = true;

            foreach (var item in itens)
            {
                var r = CalcularItem(item.Quantidade, item.PrecoUnitario, item.Desconto, item.CustoUnitario);
                listaItens.Add(r);
                receitaLiquidaTotal += r.ReceitaLiquida;
                if (r.CustoTotal.HasValue)
                    custoTotal += r.CustoTotal.Value;
                else
                    temCustoParaTodos = false;
            }

            var lucroBrutoTotal = receitaLiquidaTotal - custoTotal;
            decimal? margemBruta = null;

            if (receitaLiquidaTotal > 0m)
            {
                margemBruta = Math.Round(lucroBrutoTotal / receitaLiquidaTotal * 100m, 2, MidpointRounding.AwayFromZero);
            }

            return new VendaResultado
            {
                VendaId = vendaId,
                ReceitaLiquidaTotal = receitaLiquidaTotal,
                CustoTotal = custoTotal,
                LucroBrutoTotal = lucroBrutoTotal,
                MargemBrutaPercentual = margemBruta,
                TemCustoParaTodosOsItens = temCustoParaTodos,
                Itens = listaItens
            };
        }
    }
}
