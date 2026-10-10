using System;
using System.Collections.Generic;
using System.Globalization;

namespace ControleEstoqueLoja.Data
{
    /// <summary>
    /// Funções puras para geração de SVG do gráfico de faturamento.
    /// Não depende de HTTP, banco, ViewModels nem configuração.
    /// Tudo calculado em C# decimal e formatado com CultureInfo.InvariantCulture.
    /// </summary>
    public static class GraficoSvg
    {
        private const int Largura = 720;
        private const int Altura = 260;
        private const int MargemEsq = 56;
        private const int MargemDir = 16;
        private const int MargemTopo = 16;
        private const int MargemBase = 36;
        private static readonly decimal PlotLargura = Largura - MargemEsq - MargemDir;
        private static readonly decimal PlotAltura = Altura - MargemTopo - MargemBase;

        /// <summary>
        /// Gera até 5 marcas "bonitas" no eixo Y, começando em 0.
        /// Passo em {1, 2, 2.5, 5} × 10^k. Se max = 0 devolve [0, 50, 100].
        /// </summary>
        public static List<decimal> NiceTicks(decimal max)
        {
            var resultado = new List<decimal>();
            if (max <= 0m)
            {
                resultado.Add(0m);
                resultado.Add(50m);
                resultado.Add(100m);
                return resultado;
            }

            var passosBase = new[] { 1m, 2m, 2.5m, 5m };
            var magnitude = (int)Math.Floor(Math.Log10((double)max));
            var passo = 1m;

            foreach (var p in passosBase)
            {
                var candidato = p * (decimal)Math.Pow(10, magnitude);
                var numTicks = (int)Math.Ceiling(max / candidato) + 1;
                if (numTicks <= 5)
                {
                    passo = candidato;
                    break;
                }
            }

            for (int i = 0; i <= 4; i++)
            {
                var valor = i * passo;
                if (valor > max + passo * 0.5m) break;
                resultado.Add(Math.Round(valor, 2, MidpointRounding.AwayFromZero));
            }

            if (resultado.Count == 0) resultado.Add(0m);
            return resultado;
        }

        /// <summary>
        /// Interpolação cúbica monótona (Fritsch-Carlson) para derivadas.
        /// Retorna array de inclinações (m) para cada ponto.
        /// </summary>
        private static decimal[] CalcularInclinacoes(decimal[] y)
        {
            int n = y.Length;
            var m = new decimal[n];

            if (n == 1) { m[0] = 0; return m; }
            if (n == 2) { m[0] = m[1] = y[1] - y[0]; return m; }

            // Diferenças divididas
            var delta = new decimal[n - 1];
            for (int i = 0; i < n - 1; i++)
                delta[i] = y[i + 1] - y[i];

            // Inclinações iniciais
            m[0] = delta[0];
            m[n - 1] = delta[n - 2];
            for (int i = 1; i < n - 1; i++)
                m[i] = (delta[i - 1] + delta[i]) / 2m;

            // Fritsch-Carlson: garantir monotonicidade
            for (int i = 0; i < n - 1; i++)
            {
                if (delta[i] == 0m)
                {
                    m[i] = 0m;
                    m[i + 1] = 0m;
                }
                else
                {
                    var alpha = m[i] / delta[i];
                    var beta = m[i + 1] / delta[i];

                    if (alpha < 0m || beta < 0m)
                    {
                        m[i] = 0m;
                        m[i + 1] = 0m;
                    }
                    else
                    {
                        var h = alpha * alpha + beta * beta;
                        if (h > 9m)
                        {
                            var tau = 3m * (decimal)Math.Sqrt((double)h);
                            m[i] = tau * alpha / (decimal)Math.Sqrt((double)h) * delta[i];
                            m[i + 1] = tau * beta / (decimal)Math.Sqrt((double)h) * delta[i];
                        }
                    }
                }
            }

            return m;
        }

        /// <summary>
        /// Gera o caminho (path "d") de uma linha suavizada com interpolação cúbica
        /// monótona (Fritsch-Carlson), sem overshoot. Aceita nulos no fim.
        /// viewBox: 720×260; margens: esq 56, dir 16, topo 16, base 36.
        /// </summary>
        public static string CaminhoLinha(IReadOnlyList<decimal?> valores, decimal maxY)
        {
            var pontos = new List<(decimal x, decimal y)>();
            int n = valores.Count;
            int index = 0;

            foreach (var valor in valores)
            {
                if (!valor.HasValue) break;
                var v = valor.Value;
                decimal x = MargemEsq + (index / (decimal)(n - 1)) * PlotLargura;
                decimal y = MargemTopo + PlotAltura - (v / maxY) * PlotAltura;
                pontos.Add((x, y));
                index++;
            }

            if (pontos.Count == 0) return "";
            if (pontos.Count == 1) return $"M {Fmt(pontos[0].x)} {Fmt(pontos[0].y)}";

            var ys = pontos.Select(p => p.y).ToArray();
            var ms = CalcularInclinacoes(ys);

            var path = new System.Text.StringBuilder();
            path.Append($"M {Fmt(pontos[0].x)} {Fmt(pontos[0].y)}");

            for (int i = 0; i < pontos.Count - 1; i++)
            {
                var x0 = pontos[i].x;
                var y0 = pontos[i].y;
                var x1 = pontos[i + 1].x;
                var y1 = pontos[i + 1].y;
                var m0 = ms[i];
                var m1 = ms[i + 1];

                var dx = x1 - x0;
                var c1x = x0 + dx / 3m;
                var c1y = y0 + dx / 3m * m0;
                var c2x = x1 - dx / 3m;
                var c2y = y1 - dx / 3m * m1;

                path.Append($" C {Fmt(c1x)} {Fmt(c1y)} {Fmt(c2x)} {Fmt(c2y)} {Fmt(x1)} {Fmt(y1)}");
            }

            return path.ToString();
        }

        /// <summary>
        /// Gera o caminho (path "d") de uma área preenchida sob a linha, descendo
        /// até o eixo Y=0 (base) e fechando. Mesma interpolação e viewBox.
        /// </summary>
        public static string CaminhoArea(IReadOnlyList<decimal?> valores, decimal maxY)
        {
            var linha = CaminhoLinha(valores, maxY);
            if (string.IsNullOrEmpty(linha)) return "";

            // Extrair pontos da linha para construir a área
            var pontos = new List<(decimal x, decimal y)>();
            int n = valores.Count;
            int index = 0;

            foreach (var valor in valores)
            {
                if (!valor.HasValue) break;
                var v = valor.Value;
                decimal x = MargemEsq + (index / (decimal)(n - 1)) * PlotLargura;
                decimal y = MargemTopo + PlotAltura - (v / maxY) * PlotAltura;
                pontos.Add((x, y));
                index++;
            }

            if (pontos.Count == 0) return "";

            var path = new System.Text.StringBuilder();
            var baseY = MargemTopo + PlotAltura; // y = 0 no eixo

            path.Append($"M {Fmt(pontos[0].x)} {Fmt(baseY)}");
            path.Append($" L {Fmt(pontos[0].x)} {Fmt(pontos[0].y)}");
            path.Append(' ' + linha.Substring(1)); // pular o "M " inicial
            path.Append($" L {Fmt(pontos[^1].x)} {Fmt(baseY)} Z");

            return path.ToString();
        }

        /// <summary>
        /// Converte decimal para string com CultureInfo.InvariantCulture e no
        /// máximo 2 casas ("F2"). Garante ausência de vírgula, NaN, Infinity.
        /// </summary>
        public static string Fmt(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }
}