using System.Globalization;
using System.Text;

namespace ControleEstoqueLoja.Data;

/// <summary>
/// Monta arquivos CSV para o dono levar os dados para o Excel.
///
/// POR QUE UM HELPER E NAO UM File() COM TEXTO SOLTO: tres coisas fazem um
/// CSV "funcionar" e "virar problema" ao mesmo tempo, e todas moram aqui em
/// vez de se repetir em cada exportacao.
///
/// 1) ENCODING. O padrao do .NET e UTF-8 SEM BOM, e o Excel do Windows abre
///    isso como latin-1: todo acento vira "Leite Integral??". Por isso o BOM
///    UTF-8 e escrito no comeco do arquivo.
///
/// 2) SEPARADOR E DECIMAL. O Excel pt-BR usa ';' como separador e ',' como
///    decimal. Com ',' como separador, um preco "5,99" quebra a coluna em duas
///    e o arquivo fica inutilizavel. Entao: ';' e decimal virado com Replace,
///    nunca com ToString("N2") sem CultureInfo (que usaria o padrao do
///    servidor, e o servidor nao e o dono).
///
/// 3) CSV INJECTION (a parte de seguranca). Se um produto se chama
///    "=HYPERLINK(...)" ou "=1+1", o Excel trata como FORMULA ao abrir o
///    arquivo e pode executar algo na maquina de quem abriu. Por isso todo
///    texto que veio do usuario passa por <see cref="EscaparTexto"/>, que
///    prefixa com apostrofo o valor que comecar com caractere de formula.
///
/// IMPORTANTE: os numeros passam por <see cref="Dinheiro"/> e
/// <see cref="Quantidade"/>, que NAO usam <see cref="EscaparTexto"/> - um
/// "-5,00" legitimo seria virado em texto e pararia de somar no Excel. A
/// separacao e deliberada: so entra no escudo de formula o texto que veio do
/// cadastro, nunca um numero que nos mesmos montamos.
/// </summary>
public static class ExportacaoCsv
{
    /// <summary>Separador de campo do Excel em portugues. NAO usar virgula.</summary>
    public const char Separador = ';';

    /// <summary>Quebra de linha do CSV: CRLF, como o Excel espera.</summary>
    private const string QuebraLinha = "\r\n";

    /// <summary>
    /// Caracteres que, no inicio de uma celula, fazem o Excel interpretar o
    /// conteudo como formula (=, +, -, @ e os controles de whitespace).
    /// </summary>
    private static readonly char[] IniciosDeFormula = { '=', '+', '-', '@', '\t', '\r', '\n' };

    /// <summary>
    /// Neutraliza CSV injection em texto do usuario: se a celula comecar com um
    /// caractere de formula, prefixa com apostrofo. O Excel mostra o texto
    /// igual e NAO executa nada.
    ///
    /// Nao muda o conteudo do texto, so acrescenta o apostrofo de protecao -
    /// por isso e seguro chamar em qualquer texto livre.
    /// </summary>
    public static string EscaparTexto(string? valor)
    {
        var texto = valor ?? string.Empty;

        // Espaco inicial e o truque classico para burlar o filtro (=HYPERLINK
        // com espaco antes continua sendo formula), entao conta como inicio.
        var inicio = texto.TrimStart();

        if (inicio.Length > 0 && IniciosDeFormula.Contains(inicio[0]))
        {
            return "'" + texto;
        }

        return texto;
    }

    /// <summary>
    /// Dinheiro em pt-BR: 1234.5m -> "1.234,50". Milhar com ponto porque e o
    /// que o Excel pt-BR espera; agrupamento errado tambem quebra a leitura.
    ///
    /// O detalhe que engana aqui: o formatador do .NET decide sozinho se poe o
    /// separador de milhar, e isso muda entre versao, versao do SO e se o
    /// projeto liga globalization-invariant (aqui N2 devolveu "1234.50", sem
    /// milhar). Um CSV que aparece com milhar num PC e semoutro nao serve para
    /// comparar nota. Por isso o agrupamento e FEITO AQUI, sempre igual: as duas
    /// casas decimais vem de "F2" (que nunca agrupa) e o ponto de milhar entra
    /// por conta propria, contando de tras para frente de 3 em 3.
    /// </summary>
    public static string Dinheiro(decimal valor)
    {
        // "F2" garante duas casas e ponto decimal (InvariantCulture), sem milhar.
        var bruto = valor.ToString("F2", CultureInfo.InvariantCulture);

        var separadorDecimal = bruto.IndexOf('.');

        // O Substring comeca DEPOIS do ponto: as casas decimais sao "50", nao
        // ".50" - incluir o ponto aqui viraria "1.234,.50" no arquivo.
        var inteiro = bruto.Substring(0, separadorDecimal);
        var casas = bruto.Substring(separadorDecimal + 1);

        return $"{AgruparMilhar(inteiro)},{casas}";
    }

    /// <summary>
    /// 1234 -> "1.234" / 1234567 -> "1.234.567". Ponto a cada 3 digitos da
    /// direita para a esquerda, que e como o pt-BR escreve milhar.
    /// </summary>
    private static string AgruparMilhar(string digitos)
    {
        var negativo = digitos.StartsWith("-", StringComparison.Ordinal);
        if (negativo) digitos = digitos.Substring(1);

        var construtor = new StringBuilder();
        for (var i = 0; i < digitos.Length; i++)
        {
            // Antes de cada digito, a menos que ele ja seja o primeiro, se ainda
            // restam 3, 6, 9... digitos ate o fim, entra um ponto.
            if (i > 0 && (digitos.Length - i) % 3 == 0)
            {
                construtor.Append('.');
            }

            construtor.Append(digitos[i]);
        }

        return negativo ? "-" + construtor.ToString() : construtor.ToString();
    }

    /// <summary>
    /// Quantidade com ate 3 casas, sem zeros a direita: 0.125m -> "0,125" e
    /// 40.000m -> "40". Produto por peso tem 3 casas (0,125 kg) e por prateleira
    /// e inteiro - 3 casas cobrem os dois casos.
    /// </summary>
    public static string Quantidade(decimal valor) =>
        valor.ToString("0.###", CultureInfo.InvariantCulture).Replace(".", ",");

    /// <summary>
    /// Data/hora em pt-BR, sem hora quando ela for 00:00 (caso comum do filtro
    /// por dia): "10/02/2026 14:32" ou so "10/02/2026".
    /// </summary>
    public static string DataHora(DateTime valor) =>
        valor.TimeOfDay == TimeSpan.Zero
            ? valor.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : valor.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Monta o arquivo CSV completo: BOM UTF-8 + cabecalho + linhas.
    ///
    /// O escape ESTRUTURAL (aspas, ';' e quebra de linha dentro do valor) e
    /// aplicado aqui a todos os campos - e o que impede uma celula de
    /// "Arroz; Integral 1kg" de virar duas colunas. O escape de FORMULA e
    /// responsabilidade do <see cref="EscaparTexto"/> na hora de montar a linha.
    /// </summary>
    public static byte[] Montar(IReadOnlyList<string> cabecalhos, IEnumerable<IReadOnlyList<string>> linhas)
    {
        var sb = new StringBuilder();

        // CABECALHO: todos os titulos na MESMA linha, separados por ';'. Antes cada
        // titulo saia na sua linha - o Excel abria aquilo como 6 linhas de
        // cabecalho em vez de 1, e o arquivo ficava inutilizavel. Por isso o
        // separador entra entre os titulos e a quebra de linha so no fim.
        for (var i = 0; i < cabecalhos.Count; i++)
        {
            if (i > 0) sb.Append(Separador);
            sb.Append(Quebrar(cabecalhos[i]));
        }

        sb.Append(QuebraLinha);

        foreach (var linha in linhas)
        {
            for (var i = 0; i < linha.Count; i++)
            {
                // Separa os campos; o ultimo nao leva separador (evita coluna vazia).
                if (i > 0) sb.Append(Separador);
                sb.Append(Quebrar(linha[i]));
            }

            sb.Append(QuebraLinha);
        }

        // Preamble (BOM) gravado DENTRO do arquivo, antes de qualquer byte de
        // conteudo. Sem ele o Excel do Windows mostra acento quebrado.
        var conteudo = Encoding.UTF8.GetBytes(sb.ToString());

        var arquivo = new byte[3 + conteudo.Length];
        arquivo[0] = 0xEF;
        arquivo[1] = 0xBB;
        arquivo[2] = 0xBF;
        conteudo.CopyTo(arquivo, 3);

        return arquivo;
    }

    /// <summary>
    /// Escapa UM valor para CSV: envolve em aspas quando ele contem separador,
    /// aspas ou quebra de linha, e dobra as aspas internas (regra do RFC 4180).
    /// Sempre entre aspas tambem, por simplicidade: um CSV todo entre aspas e
    /// valido e deixa o comportamento identico em todos os campos.
    /// </summary>
    private static string Quebrar(string? valor)
    {
        var texto = (valor ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{texto}\"";
    }
}