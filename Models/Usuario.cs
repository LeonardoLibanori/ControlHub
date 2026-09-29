using Microsoft.AspNetCore.Identity;

namespace ControleEstoqueLoja.Models;

/// <summary>
/// USUARIO DO SISTEMA (login). Nao tem relacao com <see cref="Vendedor"/>,
/// que e apenas o rotulo de quem vendeu no cupom e continua independente.
///
/// Os dois lados de proposito ficam separados de proposito:
/// - Vendedor: identificacao comercial no cupom (Codigo/Ativo).
/// - Usuario: quem opera o sistema (login, senha, papel).
/// </summary>
public class Usuario : IdentityUser
{
    /// <summary>Nome exibido na tela de boas-vindas e nos relatorios.</summary>
    public string? NomeExibicao { get; set; }
}
