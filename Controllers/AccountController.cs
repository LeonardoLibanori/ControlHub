using System.ComponentModel.DataAnnotations;
using ControleEstoqueLoja.Models;
using ControleEstoqueLoja.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ControleEstoqueLoja.Controllers;

/// <summary>
/// Login, logout e a criacao do PRIMEIRO dono.
///
/// BUG-13: o controller tem [Authorize] no nivel de classe (padrao do projeto:
/// nenhuma rota fica anonima por esquecimento) e cada action deste controller
/// marca [AllowAnonymous] de proposito - e o que permite ACESSAR o login.
/// Sem esse AllowAnonymous, quem nao estivesse autenticado levaria 403 na
/// tela de login e nao conseguiria nem entrar.
///
/// Nenhuma action daqui exige policy: o login precisa funcionar ANTES de
/// existir autorizacao de dominio. O acesso real as telas continua
/// governado pelos [Authorize] das actions de negocio.
/// </summary>
[Authorize]
public class AccountController : Controller
{
    private readonly UserManager<Usuario> _userManager;
    private readonly SignInManager<Usuario> _signInManager;
    private readonly ILogger<AccountController> _log;
    private readonly LoginThrottle _throttle;

    public AccountController(
        UserManager<Usuario> userManager,
        SignInManager<Usuario> signInManager,
        ILogger<AccountController> log,
        LoginThrottle? throttle = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _log = log;
        _throttle = throttle ?? new LoginThrottle(new LoginThrottleOptions());
    }

    // =================================================================
    // LOGIN
    // =================================================================

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        // Nao faz sentido mostrar o login se o cadastro ainda nao existe:
        // o primeiro acesso precisa criar o Dono.
        if (!ExisteAlgumUsuario())
            return RedirectToAction(nameof(Setup));

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        // Contra CSRF o login reusa o mesmo token das demais POSTs.
        if (!ModelState.IsValid)
            return View(model);

        // Verifica o throttle ANTES de tocar na senha.
        var emailParaThrottle = model.Email ?? string.Empty;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        var (bloqueado, minutosRestantes, tentativasRestantes) = _throttle.Verificar(emailParaThrottle, ip);

        if (bloqueado)
        {
            ModelState.AddModelError(string.Empty,
                $"Muitas tentativas de acesso. Tente novamente em {minutosRestantes} minutos.");
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = minutosRestantes.ToString();
            return View(model);
        }

        var resultado = await _signInManager.PasswordSignInAsync(
            model.Email ?? string.Empty, model.Senha, isPersistent: true, lockoutOnFailure: true);

        if (resultado.Succeeded)
        {
            _throttle.RegistrarSucesso(emailParaThrottle, ip);
            _log.LogInformation("Login efetuado: {Email}", model.Email);
            // So aceita returnUrl local: sem isso um link externo poderia
            // transformar o login em redirecionamento para phishing.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);
            return RedirectToAction("Index", "Caixa");
        }

        if (resultado.IsLockedOut)
        {
            var email = model.Email ?? string.Empty;
            var usuario = await _userManager.FindByEmailAsync(email);
            var bloqueioAte = usuario is null ? null : await _userManager.GetLockoutEndDateAsync(usuario);
            var minutosIdentidade = bloqueioAte is null || bloqueioAte <= DateTimeOffset.UtcNow
                ? 0
                : (int)Math.Ceiling((bloqueioAte.Value - DateTimeOffset.UtcNow).TotalMinutes);
            _throttle.RegistrarFalha(emailParaThrottle, ip);
            if (minutosIdentidade > 0)
            {
                ModelState.AddModelError(string.Empty,
                    "Muitas tentativas de acesso. Tente novamente em " + minutosIdentidade + " minutos.");
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Response.Headers.RetryAfter = minutosIdentidade.ToString();
            }
            else
            {
                ModelState.AddModelError(string.Empty,
                    "Conta bloqueada temporariamente por tentativas invalidas. Tente novamente em alguns minutos.");
            }
            return View(model);
        }
        else
        {
            var (bloqAposFalha, minsAposFalha, restAposFalha) = _throttle.RegistrarFalha(emailParaThrottle, ip);
            var msg = restAposFalha switch
            {
                2 => "E-mail ou senha incorretos. Restam 2 tentativas antes do bloqueio.",
                1 => "E-mail ou senha incorretos. Resta 1 tentativa antes do bloqueio.",
                0 => "Muitas tentativas de acesso. Tente novamente em 15 minutos.",
                _ => "E-mail ou senha incorretos."
            };
            ModelState.AddModelError(string.Empty, msg);
            if (bloqAposFalha)
            {
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Response.Headers.RetryAfter = minsAposFalha.ToString();
            }
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    // =================================================================
    // SETUP DO PRIMEIRO DONO
    // =================================================================

    /// <summary>
    /// Tela de criacao do PRIMEIRO usuario, disponivel SOMENTE enquanto o
    /// banco nao tem nenhum usuario. Depois dela, este acesso e 403.
    ///
    /// A senha NAO fica no codigo nem no git: e a pessoa quem a digita.
    /// (O alerta QA_S05 ja apontou segredos versionados antes; nao repetimos.)
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Setup()
    {
        if (ExisteAlgumUsuario())
            return RedirectToAction(nameof(Login));

        return View(new SetupViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Setup(SetupViewModel model)
    {
        if (ExisteAlgumUsuario())
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
            return View(model);

        var dono = new Usuario
        {
            UserName = model.Email,
            Email = model.Email,
            NomeExibicao = model.NomeExibicao,
            EmailConfirmed = true
        };

        var criado = await _userManager.CreateAsync(dono, model.Senha);
        if (!criado.Succeeded)
        {
            foreach (var erro in criado.Errors)
                ModelState.AddModelError(string.Empty, erro.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(dono, Roles.Dono);
        _log.LogInformation("Dono inicial criado: {Email}", model.Email);

        // Ja deixa autenticado: o operador nao precisa digitar tudo de novo.
        await _signInManager.SignInAsync(dono, isPersistent: true);
        return RedirectToAction("Index", "Caixa");
    }

    // =================================================================

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AcessoNegado()
    {
        // 403: o usuario ESTA autenticado mas nao tem a policy necessaria.
        ViewData["PapelAtual"] = User.Identity?.Name ?? "desconhecido";
        return View();
    }

    private bool ExisteAlgumUsuario() => _userManager.Users.Any();
}

/// <summary>Dados do formulario de login.</summary>
public class LoginViewModel
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail vÃ¡lido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Senha { get; set; } = string.Empty;
}

/// <summary>Dados do formulario de criacao do primeiro dono.</summary>
public class SetupViewModel
{
    [Required(ErrorMessage = "Informe o nome exibido.")]
    [StringLength(80, ErrorMessage = "O nome deve ter no mÃ¡ximo 80 caracteres.")]
    [Display(Name = "Nome exibido")]
    public string NomeExibicao { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail vÃ¡lido.")]
    [Display(Name = "E-mail (login)")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter no mÃ­nimo 6 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Senha { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha")]
    [Compare(nameof(Senha), ErrorMessage = "As senhas nÃ£o conferem.")]
    public string ConfirmarSenha { get; set; } = string.Empty;
}

