using System.ComponentModel.DataAnnotations;
using ControleEstoqueLoja.Data;
using ControleEstoqueLoja.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ControleEstoqueLoja.Controllers;

/// <summary>
/// GESTAO DE USUARIOS (quem entra no sistema e com qual papel).
///
/// Autorizacao DECLARATIVA: a classe inteira exige a policy
/// PodeGerenciarUsuarios (hoje = Dono). Nenhum "if (User.IsInRole(...))"
/// aqui dentro: se a regra mudar, muda em Security/Policies.cs e nenhuma
/// linha deste arquivo precisa ser tocada.
///
/// Por que a policy esta na CLASSE e nao em cada action: este controller so
/// faz coisas administrativas. Nao existe action neutra a deixar de fora, e
/// na classe garante que uma action nova nasça protegida por padrao - o erro
/// perigoso seria o contrario (action nova sem [Authorize]).
///
/// O POST de criar tem [ValidateAntiForgeryToken] como todos os POSTs de
/// mudanca de estado do projeto: sem ele, um site externo aberto no
/// navegador do Dono conseguiria criar uma conta com senha conhecida.
/// </summary>
[Authorize(Policy = Policies.PodeGerenciarUsuarios)]
public class UsuariosController : Controller
{
    private readonly UserManager<Usuario> _userManager;
    private readonly SignInManager<Usuario> _signInManager;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<UsuariosController> _log;

    public UsuariosController(
        UserManager<Usuario> userManager,
        SignInManager<Usuario> signInManager,
        ApplicationDbContext db,
        ILogger<UsuariosController> log)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _log = log;
    }

    // =================================================================
    // LISTAGEM
    // =================================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var usuarios = _userManager.Users
            .OrderBy(u => u.NomeExibicao ?? u.Email)
            .ToList();

        var lista = new List<UsuarioListaViewModel>(usuarios.Count);
        foreach (var u in usuarios)
        {
            // Uma consulta por usuario e aceitavel: sao poucas contas de
            // balcao. Trocar por juncao com AspNetUserRoles traria SQL cru
            // para um controller que hoje so fala via UserManager.
            var papeis = await _userManager.GetRolesAsync(u);
            lista.Add(new UsuarioListaViewModel
            {
                Id = u.Id,
                Nome = string.IsNullOrWhiteSpace(u.NomeExibicao) ? "(sem nome)" : u.NomeExibicao!,
                Email = u.Email ?? "(sem e-mail)",
                Papeis = papeis.OrderBy(p => p).ToList(),
                Bloqueado = u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow
            });
        }

        return View(lista);
    }

    // =================================================================
    // EDITAR CONTA
    // =================================================================

    [HttpGet]
    public async Task<IActionResult> Editar(string id)
    {
        var usuario = await _userManager.FindByIdAsync(id);
        if (usuario is null)
            return NotFound();

        var papeis = await _userManager.GetRolesAsync(usuario);
        return View(new EditarUsuarioViewModel
        {
            Id = usuario.Id,
            Nome = usuario.NomeExibicao ?? string.Empty,
            Email = usuario.Email ?? string.Empty,
            Papel = papeis.Contains(Roles.Dono) ? Roles.Dono : Roles.Operador
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(EditarUsuarioViewModel model)
    {
        if (model.Papel != Roles.Dono && model.Papel != Roles.Operador)
            ModelState.AddModelError(nameof(model.Papel), "Selecione um papel válido.");

        var senhaInformada = !string.IsNullOrEmpty(model.NovaSenha);
        var confirmacaoInformada = !string.IsNullOrEmpty(model.ConfirmarNovaSenha);
        if (senhaInformada != confirmacaoInformada ||
            (senhaInformada && model.NovaSenha != model.ConfirmarNovaSenha))
        {
            ModelState.AddModelError(nameof(model.ConfirmarNovaSenha), "As senhas não conferem.");
        }

        if (!ModelState.IsValid)
            return View(model);

        var usuario = await _userManager.FindByIdAsync(model.Id);
        if (usuario is null)
            return NotFound();

        var email = model.Email?.Trim() ?? string.Empty;
        var emailAlterado = !string.Equals(usuario.Email, email, StringComparison.Ordinal) ||
                            !string.Equals(usuario.UserName, email, StringComparison.Ordinal);
        var nomeAlterado = !string.Equals(usuario.NomeExibicao, model.Nome, StringComparison.Ordinal);
        var idUsuarioAtual = _userManager.GetUserId(User);
        var editaPropriaConta = usuario.Id == idUsuarioAtual;

        var papelOposto = model.Papel == Roles.Dono ? Roles.Operador : Roles.Dono;
        await using var transacao = await _db.Database.BeginTransactionAsync();
        var papeisAtuais = await _userManager.GetRolesAsync(usuario);
        var papelAlterado = !papeisAtuais.Contains(model.Papel) || papeisAtuais.Contains(papelOposto);

        if (model.Papel == Roles.Operador && papeisAtuais.Contains(Roles.Dono))
        {
            var donos = await _userManager.GetUsersInRoleAsync(Roles.Dono);
            if (donos.Count == 1)
            {
                ModelState.AddModelError(nameof(model.Papel),
                    "Esta é a única conta Dono. Crie outro Dono antes de rebaixar esta.");
                return View(model);
            }
        }

        var qualquerAlteracao = emailAlterado || nomeAlterado || papelAlterado || senhaInformada;
        if (emailAlterado)
        {
            usuario.Email = email;
            usuario.UserName = email;
            usuario.EmailConfirmed = true;
        }

        usuario.NomeExibicao = model.Nome;
        var usuarioAtualizado = await _userManager.UpdateAsync(usuario);
        if (!usuarioAtualizado.Succeeded)
        {
            foreach (var erro in usuarioAtualizado.Errors)
                ModelState.AddModelError(string.Empty, MensagemAmigavel(erro));
            return View(model);
        }

        if (papelAlterado)
        {
            if (!papeisAtuais.Contains(model.Papel))
            {
                var adicionado = await _userManager.AddToRoleAsync(usuario, model.Papel);
                if (!adicionado.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Não foi possível atualizar o papel da conta.");
                    return View(model);
                }
            }

            if (papeisAtuais.Contains(papelOposto))
            {
                var removido = await _userManager.RemoveFromRoleAsync(usuario, papelOposto);
                if (!removido.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Não foi possível atualizar o papel da conta.");
                    return View(model);
                }
            }
        }

        if (senhaInformada)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
            var senhaAtualizada = await _userManager.ResetPasswordAsync(usuario, token, model.NovaSenha!);
            if (!senhaAtualizada.Succeeded)
            {
                foreach (var erro in senhaAtualizada.Errors)
                    ModelState.AddModelError(nameof(model.NovaSenha), MensagemAmigavel(erro));
                return View(model);
            }
        }

        if (emailAlterado || papelAlterado || senhaInformada)
        {
            var stampAtualizado = await _userManager.UpdateSecurityStampAsync(usuario);
            if (!stampAtualizado.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Não foi possível invalidar as sessões anteriores.");
                return View(model);
            }
        }

        await transacao.CommitAsync();

        if (editaPropriaConta && qualquerAlteracao)
            await _signInManager.RefreshSignInAsync(usuario);

        TempData["Aviso"] = $"Conta atualizada: {model.Nome}.";
        if (editaPropriaConta && model.Papel == Roles.Operador)
            return Redirect("/Caixa/Index");

        return RedirectToAction(nameof(Index));
    }

    // =================================================================
    // CRIAR CONTA
    // =================================================================

    [HttpGet]
    public IActionResult Criar() => View(new CriarUsuarioViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(CriarUsuarioViewModel model)
    {
        // O PAPEL VEM DO FORMULARIO, ENTAO NAO SE CONFIA NELE: qualquer
        // string fora da lista conhecida (papel inventado, "Dono " com
        // espaco) e descartada antes de tocar no Identity. Sem isso o campo
        // seria uma porta para gravar papel arbitrario no banco.
        if (model.Papel != Roles.Dono && model.Papel != Roles.Operador)
            ModelState.AddModelError(nameof(model.Papel), "Selecione um papel válido.");

        if (!ModelState.IsValid)
            return View(model);

        var novo = new Usuario
        {
            // UserName = Email: o login e por e-mail. NomeExibicao e so
            // apresentacao e nao interfere na autenticacao.
            UserName = model.Email,
            Email = model.Email,
            NomeExibicao = model.Nome,
            EmailConfirmed = true
        };

        var criado = await _userManager.CreateAsync(novo, model.Senha);
        if (!criado.Succeeded)
        {
            foreach (var erro in criado.Errors)
                ModelState.AddModelError(string.Empty, MensagemAmigavel(erro));

            // Log do motivo real (pelo codigo) e NUNCA da senha digitada.
            _log.LogWarning("Falha ao criar usuario {Email}: {Erros}",
                model.Email, string.Join(" | ", criado.Errors.Select(e => e.Code)));

            return View(model);
        }

        await _userManager.AddToRoleAsync(novo, model.Papel);
        _log.LogInformation("Usuario {Email} criado com o papel {Papel}",
            model.Email, model.Papel);

        // NAO autentica como o usuario recem-criado: quem esta no balcao
        // continua sendo o Dono que fez o cadastro.
        TempData["Aviso"] = $"Conta criada: {model.Nome} ({model.Papel}).";
        return RedirectToAction(nameof(Index));
    }

    // =================================================================
    // MENSAGENS
    // =================================================================

    /// <summary>
    /// O Identity responde em INGLES ("Passwords must have at least one
    /// uppercase..."). Numa loja em portugues isso vira ruido no balcao, entao
    /// os casos que acontecem de verdade tem texto proprio. O que nao estiver
    /// mapeado cai na descricao original - melhor uma mensagem estranha do
    /// que nenhuma.
    /// </summary>
    private static string MensagemAmigavel(IdentityError erro) => erro.Code switch
    {
        nameof(IdentityErrorDescriber.DuplicateUserName) or
        nameof(IdentityErrorDescriber.DuplicateEmail) =>
            "Já existe uma conta com este e-mail.",
        nameof(IdentityErrorDescriber.PasswordTooShort) =>
            "A senha é curta demais: use no mínimo 6 caracteres.",
        nameof(IdentityErrorDescriber.PasswordRequiresUpper) =>
            "A senha precisa de pelo menos uma letra MAIÚSCULA.",
        nameof(IdentityErrorDescriber.PasswordRequiresLower) =>
            "A senha precisa de pelo menos uma letra minúscula.",
        nameof(IdentityErrorDescriber.PasswordRequiresDigit) =>
            "A senha precisa de pelo menos um número.",
        nameof(IdentityErrorDescriber.InvalidUserName) or
        nameof(IdentityErrorDescriber.InvalidEmail) =>
            "E-mail inválido.",
        _ => erro.Description
    };
}

/// <summary>Uma linha da listagem de usuario (nome, e-mail e papel).</summary>
public class UsuarioListaViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<string> Papeis { get; set; } = new List<string>();

    /// <summary>Conta travada pelo lockout do Identity (tentativas erradas).</summary>
    public bool Bloqueado { get; set; }
}

/// <summary>Dados do formulario de criacao de conta.</summary>
public class CriarUsuarioViewModel
{
    [Required(ErrorMessage = "Informe o nome do usuário.")]
    [StringLength(80, ErrorMessage = "O nome deve ter no máximo 80 caracteres.")]
    [Display(Name = "Nome exibido")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(120, ErrorMessage = "O e-mail deve ter no máximo 120 caracteres.")]
    [Display(Name = "E-mail (login)")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter no mínimo 6 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Senha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirme a senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha")]
    [Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    public string ConfirmarSenha { get; set; } = string.Empty;

    /// <summary>
    /// Dono ou Operador. O padrao e o MENOR privilegio (Operador): quem cria
    /// conta para o balcao nao deve ganhar acesso financeiro por descuido.
    /// </summary>
    [Required(ErrorMessage = "Selecione o papel.")]
    [Display(Name = "Papel")]
    public string Papel { get; set; } = Roles.Operador;
}

/// <summary>Dados editaveis de uma conta existente.</summary>
public class EditarUsuarioViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o nome do usuário.")]
    [StringLength(80, ErrorMessage = "O nome deve ter no máximo 80 caracteres.")]
    [Display(Name = "Nome exibido")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(120, ErrorMessage = "O e-mail deve ter no máximo 120 caracteres.")]
    [Display(Name = "E-mail (login)")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione o papel.")]
    [Display(Name = "Papel")]
    public string Papel { get; set; } = Roles.Operador;

    [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter no mínimo 6 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nova senha (opcional)")]
    public string? NovaSenha { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NovaSenha), ErrorMessage = "As senhas não conferem.")]
    [Display(Name = "Confirmar nova senha")]
    public string? ConfirmarNovaSenha { get; set; }
}
