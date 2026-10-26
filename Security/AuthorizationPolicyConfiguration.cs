using ControleEstoqueLoja;
using Microsoft.AspNetCore.Authorization;

namespace ControleEstoqueLoja.Security;

public static class AuthorizationPolicyConfiguration
{
    public static AuthorizationBuilder AddControlHubPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(Policies.PodeOperarPDV, policy => policy.RequireRole(Roles.Operador, Roles.Dono))
        .AddPolicy(Policies.PodeReimprimirCupom, policy => policy.RequireRole(Roles.Operador, Roles.Dono))
        .AddPolicy(Policies.PodeRegistrarSuprimento, policy => policy.RequireRole(Roles.Operador, Roles.Dono))
        .AddPolicy(Policies.PodeRegistrarSangria, policy => policy.RequireRole(Roles.Operador, Roles.Dono))
        .AddPolicy(Policies.PodeVerFinanceiro, policy => policy.RequireRole(Roles.Dono))
        .AddPolicy(Policies.PodeFecharCaixa, policy => policy.RequireRole(Roles.Operador, Roles.Dono))
        .AddPolicy(Policies.PodeVerRelatorioCaixa, policy => policy.RequireRole(Roles.Operador, Roles.Dono))
        .AddPolicy(Policies.PodeGerenciarEstoque, policy => policy.RequireRole(Roles.Dono))
        .AddPolicy(Policies.PodeGerenciarVendedores, policy => policy.RequireRole(Roles.Dono))
        .AddPolicy(Policies.PodeGerenciarUsuarios, policy => policy.RequireRole(Roles.Dono))
        .AddPolicy(Policies.PodeAcessarAdmin, policy => policy.RequireRole(Roles.Dono))
        .AddPolicy(Policies.PodeUsarIA, policy => policy.RequireRole(Roles.Dono));
}
