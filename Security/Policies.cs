namespace ControleEstoqueLoja;

/// <summary>
/// Papeis (roles) do sistema. Sao strings literais de verdade porque o
/// UserManager as grava em AspNetRoles e o cookie de autenticacao as carrega
/// como claims. Nao usar enums aqui: o Identity resolve por nome.
/// </summary>
public static class Roles
{
    /// <summary>Acesso total: financeiro, estoque, caixa e vendedores.</summary>
    public const string Dono = "Dono";

    /// <summary>Operação do PDV e do caixa: vendas, movimentações e fechamento.</summary>
    public const string Operador = "Operador";
}

/// <summary>
/// POLITICAS DE AUTORIZACAO - fonte unica de verdade.
///
/// Por que policies e nao "if (User.IsInRole(...))" espalhado no codigo:
/// cada regra vira um NOME, usado em tres lugares que nunca divergem:
///   1) nas actions:      [Authorize(Policy = Policies.PodeVerFinanceiro)]
///   2) nos testes:       auth.AuthorizeAsync(user, Policies.PodeVerFinanceiro)
///   3) nas views:        await auth.AuthorizeAsync(User, Policies.PodeVerFinanceiro)
/// Assim "Operador ve o relatorio?" tem UMA resposta, nao tres.
///
/// EXTENSIBILIDADE (camada de IA que vem depois): para liberar o copiloto
/// basta declarar uma policy nova e aplicar [Authorize] apenas nos endpoints
/// de IA. Nenhuma policy existente e reescrita e nenhuma rota de producao
/// muda - e o que este arquivo existe para garantir.
/// </summary>
public static class Policies
{
    // ---------- PDV (operacao de balcao) ----------

    /// <summary>Usar o PDV: buscar produto, vender, cupom, detalhes da venda.</summary>
    public const string PodeOperarPDV = "PodeOperarPDV";

    /// <summary>Reimprimir cupom e relatorio da propria venda.</summary>
    public const string PodeReimprimirCupom = "PodeReimprimirCupom";

    // ---------- Caixa ----------

    /// <summary>Registrar SUPRIMENTO (dinheiro que ENTRA na gaveta).</summary>
    public const string PodeRegistrarSuprimento = "PodeRegistrarSuprimento";

    /// <summary>Registrar SANGRIA (dinheiro que SAI da gaveta). Dono e Operador.</summary>
    public const string PodeRegistrarSangria = "PodeRegistrarSangria";

    /// <summary>Ver histórico, dashboard e relatórios financeiros. Dono apenas.</summary>
    public const string PodeVerFinanceiro = "PodeVerFinanceiro";

    /// <summary>Consultar e encerrar o caixa do dia. Dono e Operador.</summary>
    public const string PodeFecharCaixa = "PodeFecharCaixa";

    /// <summary>Consultar/imprimir o resumo do caixa do dia. Dono e Operador.</summary>
    public const string PodeVerRelatorioCaixa = "PodeVerRelatorioCaixa";

    // ---------- Estoque e cadastros ----------

    /// <summary>Criar, editar, excluir e ajustar estoque. Dono apenas.</summary>
    public const string PodeGerenciarEstoque = "PodeGerenciarEstoque";

    /// <summary>Cadastrar e desativar vendedores (cupons). Dono apenas.</summary>
    public const string PodeGerenciarVendedores = "PodeGerenciarVendedores";

    /// <summary>
    /// Criar contas de acesso (login) e ver quem tem qual papel. Dono apenas.
    ///
    /// Nao e a mesma coisa que PodeGerenciarVendedores, apesar das duas serem
    /// "Dono apenas": Vendedor e o rotulo comercial que sai no cupom (nao
    /// autentica nada) e Usuario e a conta que ENTRA no sistema. Quem cria
    /// conta cria tambem a possibilidade de dar acesso de Dono a alguem -
    /// por isso e uma policy propria, para poder ser revista sozinha.
    /// </summary>
    public const string PodeGerenciarUsuarios = "PodeGerenciarUsuarios";

    /// <summary>Ver o menu administrativo (Alt+M). Dono apenas.</summary>
    public const string PodeAcessarAdmin = "PodeAcessarAdmin";

    // ---------- Camada de IA (futuro) ----------

    /// <summary>
    /// Acesso aos dados de financeiro/estoque pelo copiloto de IA.
    /// DECLARADA E REGISTRADA desde ja (Dono apenas), mas NENHUMA action a usa
    /// hoje: e a trava de espera para a proxima fase. Quando a IA chegar, so
    /// novidade: implementar os endpoints e repetir o padrao
    /// [Authorize(Policy = Policies.PodeUsarIA)].
    /// </summary>
    public const string PodeUsarIA = "PodeUsarIA";
}
