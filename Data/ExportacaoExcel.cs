using ClosedXML.Excel;
using ControleEstoqueLoja.Models;
using System.Globalization;
using System.Text;

namespace ControleEstoqueLoja.Data;

/// <summary>
/// ETAPA A (ControlHub): exportacao em Excel (.xlsx) ao lado do CSV.
///
/// POR QUE EXISTE, SE JA TEM CSV: o CSV e um retrato - nao soma, nao tem
/// formato de moeda, nao guarda data como data (10/02/2026 vira texto) e a
/// coluna de preco so "vira numero" se o Excel adivinhar a cultura. Quem usa o
/// arquivo para decidir compra quer o contrario: colunas que SOMAM, preco ja
/// formatado em R$, data que ordena de verdade e uma linha de totais.
///
/// O QUE NAO MUDA: o CSV continua intacto (ExportacaoCsv.cs, suas rotas e seus
/// 28 testes), porque ele e a saida que abre em qualquer programa - o Excel e o
/// que abre bem no Excel do dono.
///
/// REGRA DE OURO (a mesma do CSV): a planilha respeita o MESMO filtro da tela.
/// Se a Index mostra 3 produtos com a busca "leite", a planilha tem exatamente
/// esses 3 - nada de exportar o estoque inteiro.
///
/// SEGURANCA (a mesma do CSV): texto que veio do cadastro (nome do produto,
/// metodo de pagamento, cupom do vendedor) passa por
/// <see cref="ExportacaoCsv.EscaparTexto"/> E e gravado como TIPO TEXTO
/// EXPLICITO. O apostrofo sozinho nao basta aqui: se a celula fosse gravada sem
/// tipo, o Excel poderia reinterpretar "=1+1" como formula.
///
/// NUMERO NUNCA VIRA TEXTO: dinheiro entra como decimal e Quantidade como
/// decimal - quem formata e o NumberFormat da celula, nao o ToString. Gravar
/// "5,99" como string mataria o SUM que justifica a planilha existir.
/// </summary>
public static class ExportacaoExcel
{
    /// <summary>
    /// Content-Type do .xlsx. E o que faz o navegador BAIXAR em vez de tentar
    /// abrir; sem ele o dono recebe um arquivo sem extensao util.
    /// </summary>
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---------------- abas ----------------

    /// <summary>Aba da planilha de estoque.</summary>
    public const string AbaEstoque = "Estoque";

    /// <summary>Aba do historico de vendas.</summary>
    public const string AbaVendas = "Vendas";

    // ---------------- titulos das colunas (linha 4) ----------------

    /// <summary>
    /// Cabecalho da aba Estoque - a MESMA ordem do CSV atual, com duas
    /// diferencas: "Unidade" (kg para PorPeso, un para o resto) e "Valor em
    /// estoque (R$)", que no CSV nao existia porque la o numero nao somaria.
    /// </summary>
    public static readonly string[] CabecalhoEstoque =
    {
        "Produto", "Tipo", "Unidade", "Preço (R$)", "Estoque",
        "Estoque mínimo", "Estoque baixo", "Valor em estoque (R$)"
    };

    /// <summary>Cabecalho da aba Vendas - identico ao CSV atual, para o dono nao reaprender a ler.</summary>
    public static readonly string[] CabecalhoVendas =
    {
        "Venda", "Data", "Itens", "Quantidade total", "Método de pagamento",
        "Vendedor (cupom)", "Valor recebido", "Troco", "Total (R$)"
    };

    // ---------------- formato das celulas ----------------

    /// <summary>Dinheiro como NUMERO com formato de real: 1234.5m -> "R$ 1.234,50".</summary>
    public const string FormatoDinheiro = "R$ #,##0.00";

    /// <summary>Quantidade inteira: sem casas decimais.</summary>
    public const string FormatoQuantidadeInteira = "#,##0";

    /// <summary>Quantidade fracionaria (produto por peso, 0,125 kg): 3 casas.</summary>
    public const string FormatoQuantidadeFracionaria = "#,##0.000";

    /// <summary>Data/hora real na celula (DateTime, nao texto) com este formato de exibicao.</summary>
    public const string FormatoDataHora = "dd/MM/yyyy HH:mm";

    // ---------------- visual (cores do tema do sistema) ----------------

    /// <summary>Fundo do titulo e do cabecalho da tabela (petroleo do tema).</summary>
    public const string CorCabecalho = "#1F4E5F";

    /// <summary>Fundo da zebra (linhas alternadas) - so fundo, o texto continua escuro.</summary>
    public const string CorZebra = "#F2F7F8";

    /// <summary>Fundo da linha de totais.</summary>
    public const string CorTotal = "#D9E8EC";

    /// <summary>Fundo da linha de produto com estoque ABAIXO do minimo.</summary>
    public const string CorEstoqueBaixoFundo = "#FDE2E2";

    /// <summary>Texto da linha de produto com estoque abaixo do minimo.</summary>
    public const string CorEstoqueBaixoTexto = "#9B1C1C";

    /// <summary>Cor do TEXTO do cabecalho da tabela: branco sobre o petroleo.</summary>
    public const string CorCabecalhoTexto = "#FFFFFF";

    // ---------------- larguras ----------------

    /// <summary>Largura minima de coluna: evita "un"/"kg" espremidos a ponto de sumir.</summary>
    public const double LarguraMinima = 8;

    /// <summary>Largura maxima de coluna: evita um nome longo virando uma faixa larga.</summary>
    public const double LarguraMaxima = 60;

    // ---------------- estrutura vertical (igual nas duas abas) ----------------

    /// <summary>Linha 1: nome da marca (e a loja ao lado, quando configurada).</summary>
    public const int LinhaMarca = 1;

    /// <summary>Linha 2: "Estoque, gerado em dd/MM/yyyy às HH:mm" + filtro ativo da tela.</summary>
    public const int LinhaSubtitulo = 2;

    /// <summary>Linha 3 fica em branco de proposito: separa o titulo do cabecalho.</summary>
    public const int LinhaVazia = 3;

    /// <summary>Linha 4: cabecalho da tabela. Os dados comecam na 5.</summary>
    public const int LinhaCabecalho = 4;

    /// <summary>Primeira linha de dados (logo abaixo do cabecalho).</summary>
    public const int PrimeiraLinhaDados = LinhaCabecalho + 1;

    // =====================================================================
    // GERADORES - esqueleto da Etapa A (implementacao fica para a proxima etapa)
    // =====================================================================

    /// <summary>
    /// Gera o .xlsx do ESTOQUE.
    ///
    /// LAYOUT:
    ///   Linha 1      marca (<paramref name="marca"/>), com a loja ao lado quando
    ///                <paramref name="loja"/> nao for vazio.
    ///   Linha 2      "Estoque, gerado em dd/MM/yyyy às HH:mm" + filtro ativo no
    ///                formato: Busca: "leite". Sem filtro, so a frase.
    ///   Linha 4      <see cref="CabecalhoEstoque"/>.
    ///   Linhas 5..N  um produto por linha, na ordem recebida (alfabetica, a
    ///                mesma do CSV).
    ///   Ultima linha totais, com formula REAL de SUM sobre "Valor em estoque".
    ///
    /// VALOR EM ESTOQUE = Preco x Quantidade gravado como FORMULA (nao como
    /// numero congelado), para o dono ver de onde veio o total.
    ///
    /// ESTOQUE BAIXO: usa <see cref="Produto.EstoqueAbaixoDoMinimo"/> - a regra 11
    /// (minimo 0 ou nulo NUNCA alerta) mora no modelo e nao se reimplementa aqui.
    /// A linha inteira fica com fundo <see cref="CorEstoqueBaixoFundo"/> e texto
    /// <see cref="CorEstoqueBaixoTexto"/>.
    ///
    /// <paramref name="busca"/> e o filtro da tela: entra apenas na linha 2, como
    /// legenda de "o que este arquivo esta mostrando".
    /// </summary>
    /// <param name="produtos">Produtos ATIVOS, ja filtrados pela busca e ordenados por nome.</param>
    /// <param name="marca">Marca do topo (fallback: ControlHub).</param>
    /// <param name="loja">Loja do topo; vazio = nao aparece.</param>
    /// <param name="busca">Texto da busca ativa na tela, ou null.</param>
    /// <returns>Bytes do .xlsx, prontos para o File() do controller.</returns>
    public static byte[] Estoque(IReadOnlyList<Produto> produtos, string marca, string loja, string? busca)
    {
        using var livro = NovoLivro();
        var aba = livro.AddWorksheet(AbaEstoque);

        EscreverTitulo(aba, marca, loja);
        EscreverLegenda(aba, "Estoque", busca, "Busca");
        EscreverCabecalho(aba, CabecalhoEstoque);

        var linha = PrimeiraLinhaDados;
        var indice = 0;
        foreach (var produto in produtos)
        {
            // Fundo ANTES do texto: a linha ja nasce com a cor certa (zebra ou
            // alerta) e o texto entra por cima, sem risco de perder o quotePrefix.
            AplicarFundo(aba, linha, CabecalhoEstoque.Length, produto.EstoqueAbaixoDoMinimo, indice);

            // Coluna A - nome, texto de usuario (escudo de formula).
            EscreverTexto(aba.Cell(linha, 1), produto.Nome);

            // Coluna B - tipo; Coluna C - unidade (kg so para PorPeso).
            EscreverTexto(aba.Cell(linha, 2), produto.Tipo.ToString());
            EscreverTexto(aba.Cell(linha, 3), Unidade(produto.Tipo));

            // Coluna D - preco como NUMERO (Preco ja e decimal em reais).
            EscreverNumero(aba.Cell(linha, 4), produto.Preco, FormatoDinheiro);

            // Coluna E - estoque como numero (inteiro ou fracionario pelo valor).
            EscreverNumero(aba.Cell(linha, 5), produto.Quantidade, FormatoQuantidade(produto.Quantidade));

            // Coluna F - estoque minimo, so quando configurado (null = celula vazia).
            if (produto.EstoqueMinimo.HasValue)
            {
                EscreverNumero(aba.Cell(linha, 6), produto.EstoqueMinimo.Value,
                    FormatoQuantidade(produto.EstoqueMinimo.Value));
            }

            // Coluna G - o alerta como texto, para o dono filtrar no Excel.
            EscreverTexto(aba.Cell(linha, 7), produto.EstoqueAbaixoDoMinimo ? "SIM" : "NAO");

            // Coluna H - VALOR EM ESTOQUE como FORMULA (Preco x Quantidade):
            // o dono corrige o estoque e o total acompanha.
            var valor = aba.Cell(linha, 8);
            valor.FormulaA1 = $"D{linha}*E{linha}";
            valor.Style.NumberFormat.Format = FormatoDinheiro;

            linha++;
            indice++;
        }

        var ultimaDados = linha - 1;

        if (produtos.Count > 0)
        {
            EscreverTotaisEstoque(aba, PrimeiraLinhaDados, ultimaDados);
        }

        Finalizar(aba, CabecalhoEstoque.Length, ultimaDados);
        return Salvar(livro);
    }

    /// <summary>
    /// Gera o .xlsx do HISTORICO DE VENDAS.
    ///
    /// LAYOUT igual ao de <see cref="Estoque"/>, com <see cref="CabecalhoVendas"/>
    /// e a legenda "Vendas, gerado em dd/MM/yyyy às HH:mm" + "Dia: dd/MM/yyyy"
    /// quando houver filtro.
    ///
    /// O TOTAL NUNCA E RECALCULADO: sai <see cref="Venda.TotalVenda"/> como esta
    /// gravado. Se o total da tela for ajustado a mao, a planilha continua
    /// mostrando o que o caixa realmente fechou - a mesma regra de ouro do CSV.
    ///
    /// TOTAIS ao fim: contagem de vendas e SUM da coluna Total (R$).
    ///
    /// Data entra como DateTime DE VERDADE (nao texto): e o que permite ao dono
    /// ordenar e filtrar por periodo dentro do Excel.
    /// </summary>
    /// <param name="vendas">Vendas ja filtradas pelo dia (ou todas) e ordenadas.</param>
    /// <param name="marca">Marca do topo (fallback: ControlHub).</param>
    /// <param name="loja">Loja do topo; vazio = nao aparece.</param>
    /// <param name="data">Dia filtrado na tela, ou null = todas as vendas.</param>
    /// <returns>Bytes do .xlsx, prontos para o File() do controller.</returns>
    public static byte[] Historico(IReadOnlyList<Venda> vendas, string marca, string loja, DateTime? data)
    {
        using var livro = NovoLivro();
        var aba = livro.AddWorksheet(AbaVendas);

        EscreverTitulo(aba, marca, loja);
        EscreverLegenda(aba, "Vendas",
            data?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), "Dia");
        EscreverCabecalho(aba, CabecalhoVendas);

        var linha = PrimeiraLinhaDados;
        var indice = 0;
        foreach (var venda in vendas)
        {
            AplicarFundo(aba, linha, CabecalhoVendas.Length, false, indice);

            // Coluna A - id da venda: NUMERO, para o COUNT da linha de totais
            // realmente contar vendas.
            EscreverNumero(aba.Cell(linha, 1), venda.Id, FormatoQuantidadeInteira);

            // Coluna B - data como DateTime de verdade (ordena/filtra no Excel).
            EscreverNumero(aba.Cell(linha, 2), venda.DataVenda, FormatoDataHora);

            // Coluna C - quantidade de itens: inteira.
            EscreverNumero(aba.Cell(linha, 3), venda.Itens.Count, FormatoQuantidadeInteira);

            // Coluna D - quantidade total de itens: 1 kg e 5 caixas somam aqui.
            var quantidade = venda.Itens.Sum(item => item.Quantidade);
            EscreverNumero(aba.Cell(linha, 4), quantidade, FormatoQuantidade(quantidade));

            // Coluna E/F - metodo de pagamento e cupom do vendedor: texto de usuario.
            EscreverTexto(aba.Cell(linha, 5), venda.MetodoPagamento);
            EscreverTexto(aba.Cell(linha, 6), venda.CodigoVendedor);

            // Coluna G/H/I - dinheiro COMO ESTA GRAVADO (nunca recalculado).
            EscreverNumero(aba.Cell(linha, 7), venda.ValorRecebido, FormatoDinheiro);
            EscreverNumero(aba.Cell(linha, 8), venda.ValorTroco, FormatoDinheiro);
            EscreverNumero(aba.Cell(linha, 9), venda.TotalVenda, FormatoDinheiro);

            linha++;
            indice++;
        }

        var ultimaDados = linha - 1;

        if (vendas.Count > 0)
        {
            EscreverTotaisVendas(aba, PrimeiraLinhaDados, ultimaDados);
        }

        Finalizar(aba, CabecalhoVendas.Length, ultimaDados);
        return Salvar(livro);
    }

    // =====================================================================
    // AUXILIARES - as pecas visuais compartilhadas pelas duas abas
    // =====================================================================

    /// <summary>
    /// Livro novo com <see cref="XLWorkbook.FullCalculationOnLoad"/> ligado: sem
    /// isso o Excel pode abrir com as formulas SUM/COUNT ainda sem cache e o
    /// dono le zero no total na primeira carga.
    /// </summary>
    private static XLWorkbook NovoLivro()
        => new() { FullCalculationOnLoad = true };

    /// <summary>Grava o livro em memoria e devolve os bytes do .xlsx.</summary>
    private static byte[] Salvar(XLWorkbook livro)
    {
        using var memoria = new MemoryStream();
        livro.SaveAs(memoria);
        return memoria.ToArray();
    }

    /// <summary>
    /// Linha 1: marca em A1 (fonte petroleo e negrito) e a loja em B1 so quando
    /// configurada. Marca vazia cai no padrao "ControlHub" - o arquivo nunca
    /// sai sem identificacao. Loja em branco nao escreve nada (evita o titulo
    /// vazio grudado na marca).
    /// </summary>
    private static void EscreverTitulo(IXLWorksheet aba, string marca, string loja)
    {
        var celula = aba.Cell(LinhaMarca, 1);
        celula.Value = ExportacaoCsv.EscaparTexto(
            string.IsNullOrWhiteSpace(marca) ? "ControlHub" : marca.Trim());
        celula.Style.Font.Bold = true;
        celula.Style.Font.FontColor = XLColor.FromHtml(CorCabecalho);

        if (string.IsNullOrWhiteSpace(loja))
        {
            return;
        }

        var celulaLoja = aba.Cell(LinhaMarca, 2);
        celulaLoja.Value = ExportacaoCsv.EscaparTexto(loja.Trim());
        celulaLoja.Style.Font.Bold = true;
        celulaLoja.Style.Font.FontColor = XLColor.FromHtml(CorCabecalho);
    }

    /// <summary>
    /// Linha 2: "Estoque, gerado em dd/MM/yyyy às HH:mm" e, quando houver filtro
    /// da tela, o sufixo ". Busca: \"leite\"" (ou ". Dia: ..."). E a prova de que
    /// o arquivo mostra a MESMA tela - quem abrir depois sabe o que ele cobre.
    /// </summary>
    private static void EscreverLegenda(IXLWorksheet aba, string assunto, string? filtro, string rotulo)
    {
        var agora = DateTime.Now;
        var legenda = new StringBuilder();
        legenda.Append(assunto)
               .Append(", gerado em ")
               .Append(agora.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))
               .Append(" às ")
               .Append(agora.ToString("HH:mm", CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            legenda.Append(". ").Append(rotulo).Append(": \"")
                   .Append(filtro.Trim())
                   .Append('"');
        }

        // O texto COMECO com o assunto, entao o escudo de formula nao dispara;
        // ainda assim passa pelo EscaparTexto por consistencia.
        aba.Cell(LinhaSubtitulo, 1).Value = ExportacaoCsv.EscaparTexto(legenda.ToString());
    }

    /// <summary>
    /// Linha 4: os titulos das colunas com fundo petroleo, fonte branca e
    /// negrito - a mesma identidade visual do cabecalho de toda a interface.
    /// </summary>
    private static void EscreverCabecalho(IXLWorksheet aba, IReadOnlyList<string> titulos)
    {
        for (var i = 0; i < titulos.Count; i++)
        {
            aba.Cell(LinhaCabecalho, i + 1).Value = titulos[i];
        }

        var faixa = aba.Range(LinhaCabecalho, 1, LinhaCabecalho, titulos.Count);
        faixa.Style.Fill.BackgroundColor = XLColor.FromHtml(CorCabecalho);
        faixa.Style.Font.FontColor = XLColor.FromHtml(CorCabecalhoTexto);
        faixa.Style.Font.Bold = true;
    }

    /// <summary>
    /// Fundo da linha de DADOS, antes do texto entrar. REGRA DE PRIORIDADE:
    /// estoque baixo vence a zebra (quem esta no vermelho nao pode sumir numa
    /// linha clara); a zebra cai nas linhas impares do indice (2a, 4a...).
    /// </summary>
    private static void AplicarFundo(IXLWorksheet aba, int linha, int colunas, bool alerta, int indice)
    {
        var faixa = aba.Range(linha, 1, linha, colunas);

        if (alerta)
        {
            faixa.Style.Fill.BackgroundColor = XLColor.FromHtml(CorEstoqueBaixoFundo);
            faixa.Style.Font.FontColor = XLColor.FromHtml(CorEstoqueBaixoTexto);
            return;
        }

        if (indice % 2 == 1)
        {
            faixa.Style.Fill.BackgroundColor = XLColor.FromHtml(CorZebra);
        }
    }

    /// <summary>
    /// Texto de usuario gravado como TEXTO de verdade: o EscaparTexto peca o
    /// apostrofo de protecao e o ClosedXML o transforma em quotePrefix, entao o
    /// Excel mostra "=1+1" como texto e NUNCA executa. Texto comum fica sem
    /// apostrofo (a planilha nao vira lixo visual).
    /// </summary>
    private static void EscreverTexto(IXLCell celula, string? valor)
    {
        celula.Value = ExportacaoCsv.EscaparTexto(valor);
    }

    /// <summary>
    /// Numero como NUMERO (nunca texto): quem formata e o NumberFormat da
    /// celula - e o que faz o SUM da linha de totais existir.
    /// </summary>
    private static void EscreverNumero(IXLCell celula, decimal valor, string formato)
    {
        celula.Value = valor;
        celula.Style.NumberFormat.Format = formato;
    }

    /// <summary>
    /// Data/hora como DateTime DE VERDADE (nao texto): e o que permite ordenar
    /// e filtrar por periodo dentro do Excel.
    /// </summary>
    private static void EscreverNumero(IXLCell celula, DateTime valor, string formato)
    {
        celula.Value = valor;
        celula.Style.NumberFormat.Format = formato;
    }

    /// <summary>Unidade exibida do produto: kg so para PorPeso, un para o resto.</summary>
    private static string Unidade(TipoProduto tipo)
        => tipo == TipoProduto.PorPeso ? "kg" : "un";

    /// <summary>
    /// Formato da quantidade pelo VALOR: inteiro (#,##0) ou fracionario
    /// (#,##0.000, para 0,125 kg). 3 casas cobrem os dois casos sem o Excel
    /// mostrar "40.000" para um estoque de prateleira.
    /// </summary>
    private static string FormatoQuantidade(decimal valor)
        => valor == decimal.Truncate(valor)
            ? FormatoQuantidadeInteira
            : FormatoQuantidadeFracionaria;

    /// <summary>
    /// Ultima linha da aba Estoque: rotulo "Total" em A e formula REAL de SUM
    /// sobre "Valor em estoque" (coluna H) - o dono confere o valor do estoque
    /// de uma vez so, e a formula continua acompanhando se ele corrigir a tela.
    /// </summary>
    private static void EscreverTotaisEstoque(IXLWorksheet aba, int primeira, int ultima)
    {
        var linha = ultima + 1;
        EstilizarTotais(aba, linha, CabecalhoEstoque.Length);

        aba.Cell(linha, 1).Value = "Total";

        var total = aba.Cell(linha, 8);
        total.FormulaA1 = $"SUM(H{primeira}:H{ultima})";
        total.Style.NumberFormat.Format = FormatoDinheiro;
    }

    /// <summary>
    /// Ultima linha da aba Vendas: COUNT sobre a coluna A (ids numericos =
    /// contagem de vendas) e SUM da coluna Total - a soma do que o caixa
    /// fechou, SEM recalcular venda nenhuma por conta propria.
    /// </summary>
    private static void EscreverTotaisVendas(IXLWorksheet aba, int primeira, int ultima)
    {
        var linha = ultima + 1;
        EstilizarTotais(aba, linha, CabecalhoVendas.Length);

        var contagem = aba.Cell(linha, 1);
        contagem.FormulaA1 = $"COUNT(A{primeira}:A{ultima})";
        contagem.Style.NumberFormat.Format = FormatoQuantidadeInteira;

        var soma = aba.Cell(linha, 9);
        soma.FormulaA1 = $"SUM(I{primeira}:I{ultima})";
        soma.Style.NumberFormat.Format = FormatoDinheiro;
    }

    /// <summary>
    /// Visual da linha de totais (todas as colunas, inclusive as vazias):
    /// fundo claro e negrito - a linha de fechamento tem de saltar aos olhos.
    /// </summary>
    private static void EstilizarTotais(IXLWorksheet aba, int linha, int colunas)
    {
        var faixa = aba.Range(linha, 1, linha, colunas);
        faixa.Style.Fill.BackgroundColor = XLColor.FromHtml(CorTotal);
        faixa.Style.Font.Bold = true;
    }

    /// <summary>
    /// Acabamento das duas abas, na ordem que o Excel espera:
    /// painel congelado no cabecalho, AutoFilter so ate os DADOS (os totais
    /// ficam fora, senao o filtro engolia a linha de fechamento), impressao em
    /// paisagem de 1 pagina de largura com o cabecalho repetido, e larguras
    /// ajustadas pelo conteudo a partir da linha 4 - o titulo longo da linha 1
    /// fica de fora, entao ele nao alarga a coluna A.
    /// </summary>
    private static void Finalizar(IXLWorksheet aba, int colunas, int ultimaDados)
    {
        // Painel congelado: cabecalho sempre visivel, a rolagem comeca nos dados.
        aba.SheetView.FreezeRows(LinhaCabecalho);

        // AutoFilter cobre cabecalho + dados. ultimaDados nunca fica abaixo da
        // linha 4 (sem dados vira 4, so o cabecalho).
        aba.Range(LinhaCabecalho, 1, ultimaDados, colunas).SetAutoFilter();

        // Impressao: paisagem, 1 pagina de largura, cabecalho repetido em cada pagina.
        aba.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        aba.PageSetup.PagesWide = 1;
        aba.PageSetup.PagesTall = 0;
        aba.PageSetup.SetRowsToRepeatAtTop(LinhaCabecalho, LinhaCabecalho);

        // Larguras 8..60 medidas so nas linhas 4..ultimaDados.
        aba.Columns().AdjustToContents(LinhaCabecalho, ultimaDados, LarguraMinima, LarguraMaxima);
    }
}
