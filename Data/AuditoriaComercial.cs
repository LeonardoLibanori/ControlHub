using ControleEstoqueLoja.Models;

namespace ControleEstoqueLoja.Data;

public static class AcoesAuditoria
{
    public const string AlteracaoPreco = "AlteracaoPreco";
    public const string ReposicaoEstoque = "ReposicaoEstoque";
    public const string AjusteEstoque = "AjusteEstoque";
    public const string CadastroProduto = "CadastroProduto";
    public const string ExclusaoProduto = "ExclusaoProduto";
    public const string Suprimento = "Suprimento";
    public const string Sangria = "Sangria";
    public const string FechamentoCaixa = "FechamentoCaixa";
    public const string CriacaoUsuario = "CriacaoUsuario";
    public const string EdicaoUsuario = "EdicaoUsuario";
    public const string CadastroVendedor = "CadastroVendedor";
    public const string ExclusaoVendedor = "ExclusaoVendedor";
}

/// <summary>
/// Eventos comerciais relevantes. Valores são texto curto (preço, saldo, papel);
/// senha, CPF, cookie e segredo ficam de fora de propósito.
/// </summary>
public static class AuditoriaComercial
{
    public const int TamanhoMaximoValor = 200;

    public static RegistroAuditoria Criar(
        string acao,
        string entidade,
        string? entidadeId,
        string? usuarioId,
        string? valorAnterior,
        string? valorNovo,
        string? motivo,
        string? entidadeNome = null)
    {
        return new RegistroAuditoria
        {
            DataHora = DateTime.Now,
            UsuarioId = usuarioId,
            Acao = acao,
            Entidade = entidade,
            EntidadeId = Truncar(entidadeId, 80),
            EntidadeNome = Truncar(entidadeNome, 200),
            ValorAnterior = Truncar(valorAnterior, TamanhoMaximoValor),
            ValorNovo = Truncar(valorNovo, TamanhoMaximoValor),
            Motivo = Truncar(motivo, 200)
        };
    }

    /// <summary>
    /// Dados de apresentação (rótulo, ícone e classe CSS de cor) para uma ação de
    /// auditoria. Os códigos internos (Acao) são preservados no banco e nos filtros;
    /// este mapeamento serve apenas para a View. Valores desconhecidos recebem um
    /// fallback neutro sem lançar exceção.
    /// </summary>
    public sealed class AcaoApresentacao
    {
        public string Rotulo { get; set; } = string.Empty;
        public string Icone { get; set; } = string.Empty;
        public string ClasseCss { get; set; } = "badge-acao-neutro";
    }

    /// <summary>
    /// Retorna a descrição amigável em português para um código de ação de auditoria.
    /// Os códigos internos (Acao) são preservados no banco e nos filtros; este método
    /// serve apenas para apresentação na View. Valores desconhecidos recebem um fallback
    /// legível sem lançar exceção.
    /// </summary>
    public static string NomeAmigavel(string acao)
    {
        return ObterApresentacao(acao).Rotulo;
    }

    /// <summary>
    /// Retorna o mapeamento completo de apresentação para uma ação de auditoria.
    /// Inclui rótulo amigável, ícone discreto e classe CSS da cor semântica.
    /// </summary>
    public static AcaoApresentacao ObterApresentacao(string acao)
    {
        if (string.IsNullOrWhiteSpace(acao))
            return new AcaoApresentacao
            {
                Rotulo = "—",
                Icone = "—",
                ClasseCss = "badge-acao-neutro"
            };

        return acao switch
        {
            AcoesAuditoria.ReposicaoEstoque => new AcaoApresentacao
            {
                Rotulo = "Reposição de estoque",
                Icone = "↑",
                ClasseCss = "badge-acao-reposicao"
            },
            AcoesAuditoria.CadastroProduto => new AcaoApresentacao
            {
                Rotulo = "Produto cadastrado",
                Icone = "+",
                ClasseCss = "badge-acao-cadastro"
            },
            AcoesAuditoria.ExclusaoProduto => new AcaoApresentacao
            {
                Rotulo = "Produto desativado",
                Icone = "×",
                ClasseCss = "badge-acao-exclusao"
            },
            AcoesAuditoria.AjusteEstoque => new AcaoApresentacao
            {
                Rotulo = "Ajuste de estoque",
                Icone = "↻",
                ClasseCss = "badge-acao-ajuste"
            },
            AcoesAuditoria.AlteracaoPreco => new AcaoApresentacao
            {
                Rotulo = "Alteração de preço",
                Icone = "€",
                ClasseCss = "badge-acao-ajuste"
            },
            AcoesAuditoria.Suprimento => new AcaoApresentacao
            {
                Rotulo = "Suprimento de caixa",
                Icone = "↑",
                ClasseCss = "badge-acao-suprimento"
            },
            AcoesAuditoria.Sangria => new AcaoApresentacao
            {
                Rotulo = "Sangria de caixa",
                Icone = "↓",
                ClasseCss = "badge-acao-sangria"
            },
            AcoesAuditoria.FechamentoCaixa => new AcaoApresentacao
            {
                Rotulo = "Fechamento de caixa",
                Icone = "⌘",
                ClasseCss = "badge-acao-neutro"
            },
            AcoesAuditoria.CriacaoUsuario => new AcaoApresentacao
            {
                Rotulo = "Usuário criado",
                Icone = "+",
                ClasseCss = "badge-acao-cadastro"
            },
            AcoesAuditoria.EdicaoUsuario => new AcaoApresentacao
            {
                Rotulo = "Usuário editado",
                Icone = "✎",
                ClasseCss = "badge-acao-ajuste"
            },
            AcoesAuditoria.CadastroVendedor => new AcaoApresentacao
            {
                Rotulo = "Vendedor cadastrado",
                Icone = "+",
                ClasseCss = "badge-acao-cadastro"
            },
            AcoesAuditoria.ExclusaoVendedor => new AcaoApresentacao
            {
                Rotulo = "Vendedor desativado",
                Icone = "×",
                ClasseCss = "badge-acao-exclusao"
            },
            _ => new AcaoApresentacao
            {
                Rotulo = FallbackLegivel(acao),
                Icone = "•",
                ClasseCss = "badge-acao-neutro"
            }
        };
    }

    private static string FallbackLegivel(string acao)
    {
        // Converte PascalCase para palavras separadas: "AlgoXpto" -> "Algo Xpto"
        var resultado = System.Text.RegularExpressions.Regex.Replace(
            acao,
            "([a-z])([A-Z])",
            "$1 $2");
        return string.IsNullOrWhiteSpace(resultado) ? acao : resultado;
    }

    private static string? Truncar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return valor;
        var texto = valor.Trim();
        return texto.Length <= maximo ? texto : texto[..maximo];
    }
}
