<#
.SYNOPSIS
    QA por HTTP real - autorizacao dos 3 perfis (anonimo, Operador e Dono).

.DESCRIPTION
    Sobe a aplicacao (dotnet run) contra um banco ISOLADO em %TEMP%, cria o Dono
    em /Account/Setup, cria um Operador pela tela /Usuarios e confere, por HTTP
    real, as regras de autorizacao do BUG-13 (commits 3 e 6):

      * anonimo: nenhuma rota de dominio responde 200 - tudo cai no login
      * Operador: PDV, venda, cupom e suprimento passam; o resto e 403 (AJAX)
        ou 302 para /Account/AcessoNegado (navegacao do navegador)
      * Dono: passa em tudo, inclusive /Usuarios
      * /Usuarios valida papel inventado, e-mail repetido e mensagem em portugues

    Como o navegador manda "Accept: text/html" e o fetch() do painel manda
    "X-Requested-With: XMLHttpRequest", o teste cobre os DOIS caminhos - e o que
    prova que o operador recebe a pagina de acesso negado (e nao um JSON vazio)
    quando clica, e um 403 seco quando o proprio JS tenta.

    IMPORTANTE: ao final compara o hash SHA256 do controleestoque.db VERSIONADO
    antes e depois. Se mudar, o script falha - o banco de producao jamais deve
    ser tocado por este teste.

.NOTES
    Requer ExecutionPolicy Bypass:
        powershell -ExecutionPolicy Bypass -File .\http-authz.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\http-authz.ps1
    powershell -ExecutionPolicy Bypass -File .\http-authz.ps1 -Porta 5499
#>
[CmdletBinding()]
param(
    [int]$Porta = 5400,
    [switch]$ManterApp
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- caminhos ---
$ScriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjetoDir   = Split-Path -Parent $ScriptDir            # ...\ControleEstoqueLoja
$DbVersionado = Join-Path $ProjetoDir 'controleestoque.db'
$DbIsolado    = Join-Path $env:TEMP ("qa-authz-" + [Guid]::NewGuid().ToString('N') + '.db')
$BaseUrl      = "http://127.0.0.1:$Porta"
$DumpDir      = Join-Path $env:TEMP 'qa-authz-dumps'
New-Item -ItemType Directory -Force $DumpDir | Out-Null

$global:p = 0; $global:f = 0
$codigoSaida = 0
function Say([string]$t) { Write-Host $t }
function Write-Titulo([string]$t) {
    Write-Host ''
    Write-Host "=== $t ===" -ForegroundColor Cyan
}
function Write-Ok([string]$t) { Write-Host "  [OK]    $t" -ForegroundColor Green }
function Write-Falha([string]$t) { Write-Host "  [FALHA] $t" -ForegroundColor Red }
function Chama($jar, $m, $u, $b, $tok, $ajax) {
    $q = [System.Net.HttpWebRequest]::Create("$BaseUrl$u")
    $q.Method = $m; $q.AllowAutoRedirect = $false; $q.Timeout = 30000; $q.CookieContainer = $jar
    if ($ajax) { $q.Headers.Add('X-Requested-With', 'XMLHttpRequest') }
    else { $q.Accept = 'text/html,application/xhtml+xml' }
    if ($b) {
        if ($tok) { $b = $b + '&__RequestVerificationToken=' + [uri]::EscapeDataString($tok) }
        $q.ContentType = 'application/x-www-form-urlencoded'
        $by = [System.Text.Encoding]::UTF8.GetBytes($b); $q.ContentLength = $by.Length
        $st = $q.GetRequestStream(); $st.Write($by, 0, $by.Length); $st.Close()
    }
    if ($tok) { $q.Headers.Add('X-CSRF-TOKEN', $tok) }
    try { $rs = $q.GetResponse() }
    catch [System.Net.WebException] { $rs = $_.Exception.Response; if (-not $rs) { return [pscustomobject]@{ Code = -1; Body = ''; Loc = '' } } }
    $code = [int]$rs.StatusCode; $loc = ''
    try { if ($rs.Headers['Location']) { $loc = [string]$rs.Headers['Location'] } } catch {}
    $txt = ''
    try { $sr = New-Object System.IO.StreamReader($rs.GetResponseStream(), [System.Text.Encoding]::UTF8); $txt = $sr.ReadToEnd(); $sr.Close() } catch {}
    $rs.Close()
    return [pscustomobject]@{ Code = $code; Body = $txt; Loc = $loc }
}
function Token($html) { $m = [regex]::Match($html, '<meta\s+name="csrf-token"\s+content="([^"]*)"'); if ($m.Success) { return $m.Groups[1].Value }; return $null }
function NovaSessao { New-Object System.Net.CookieContainer }
function Login($email, $senha) {
    $j = NovaSessao
    $lp = Chama $j 'GET' '/Account/Login' $null $null $false
    $tk = Token $lp.Body
    if (-not $tk) { return [pscustomobject]@{ Jar = $j; Ok = $false } }
    $b = 'Email=' + [uri]::EscapeDataString($email) + '&Senha=' + [uri]::EscapeDataString($senha)
    $r = Chama $j 'POST' '/Account/Login' $b $tk $false
    return [pscustomobject]@{ Jar = $j; Ok = ($r.Code -eq 302) }
}
function Ok($desc, $esperado, $r) {
    if ("$esperado" -eq "$($r.Code)") { $global:p++; Write-Ok ('{0} -> {1}' -f $desc, $r.Code); return }
    $global:f++; Write-Falha ('{0} -> {1} (esperado {2})' -f $desc, $r.Code, $esperado)
    if ($r.Loc) { Say ('          Location: ' + $r.Loc) }
    if ($r.Body) { Say ('          corpo: ' + $r.Body.Substring(0, [Math]::Min(150, $r.Body.Length))) }
}
function Ok302Login($desc, $r) {
    if ($r.Code -eq 302 -and $r.Loc -match '/Account/Login') { $global:p++; Write-Ok ('{0} -> 302 {1}' -f $desc, $r.Loc); return }
    $global:f++; Write-Falha ('{0} -> {1} {2} (esperado 302 para /Account/Login)' -f $desc, $r.Code, $r.Loc)
}
function Ok302Negado($desc, $r) {
    if ($r.Code -eq 302 -and $r.Loc -match 'AcessoNegado') { $global:p++; Write-Ok ('{0} -> 302 {1}' -f $desc, $r.Loc); return }
    $global:f++; Write-Falha ('{0} -> {1} {2} (esperado 302 para /Account/AcessoNegado)' -f $desc, $r.Code, $r.Loc)
}
function Nao2xx($desc, $r) {
    if ($r.Code -ge 300) { $global:p++; Write-Ok ('{0} -> {1} (bloqueado)' -f $desc, $r.Code); return }
    $global:f++; Write-Falha ('{0} -> {1} (NAO deveria passar)' -f $desc, $r.Code)
}
function TemTexto($desc, $corpo, $agulha) {
    if ($corpo -like "*$agulha*") { $global:p++; Write-Ok ('{0} contem "{1}"' -f $desc, $agulha); return }
    $global:f++; Write-Falha ('{0} NAO contem "{1}" (corpo com {2} chars)' -f $desc, $agulha, $corpo.Length)
    # Somente na falha: guarda o corpo em arquivo para inspecao manual.
    $global:dumpN = $global:dumpN + 1
    $pp = Join-Path $DumpDir ("dump-{0}.html" -f $global:dumpN)
    [IO.File]::WriteAllText($pp, $corpo, (New-Object System.Text.UTF8Encoding($false)))
    Say ('          corpo salvo em ' + $pp)
}
function Nao302($desc, $r) {
    if ($r.Code -ne 302) { $global:p++; Write-Ok ('{0} -> {1} (sem redirect: nada foi criado)' -f $desc, $r.Code); return }
    $global:f++; Write-Falha ('{0} -> 302 {1} (aceitou dado invalido!)' -f $desc, $r.Loc)
}
function SemTexto($desc, $corpo, $agulha) {
    if ($corpo -like "*$agulha*") { $global:f++; Write-Falha ('{0} contem "{1}" (nao deveria)' -f $desc, $agulha); return }
    $global:p++; Write-Ok ('{0} nao contem "{1}"' -f $desc, $agulha)
}
# Mostra, em texto, o que o navegador desenha: e a versao "visual" dos testes
# acima, para quem esta revisando ver a diferenca entre os papeis sem abrir o
# navegador. Nao conta checagem: so imprime o que foi encontrado no HTML.
function Mostrar-Menu($titulo, $html) {
    Say ''
    Say ('  -- ' + $titulo + ' --')

    $links = [regex]::Matches($html, '<a class="nav-link text-dark"[^>]*>([^<]+)</a>')
    if ($links.Count -gt 0) {
        Say ('    navbar .......: ' + (($links | ForEach-Object { $_.Groups[1].Value }) -join ' | '))
    } else {
        Say '    navbar .......: (nenhum link)'
    }

    $itens = [regex]::Matches($html, 'data-key="(\d)"[^>]*>(.*?)</button>', 'Singleline')
    if ($itens.Count -gt 0) {
        foreach ($m in $itens) {
            $rotulo = ([regex]::Matches($m.Groups[2].Value, '<span[^>]*>([^<]*)</span>') | ForEach-Object { $_.Groups[1].Value }) -join ' '
            $rotulo = ($rotulo -replace '[\uD800-\uDFFF\uFE0F]', '') -replace '\s+', ' '
            Say ('    Alt+M ' + $m.Groups[1].Value + ' .......: ' + $rotulo.Trim())
        }
    } else {
        Say '    Alt+M ........: (nenhum item)'
    }

    if ($html -match '>Sair</button>') { Say '    botao Sair ...: sim' } else { Say '    botao Sair ...: nao' }
    if ($html -match 'href="/Usuarios"') { Say '    link /Usuarios: sim' } else { Say '    link /Usuarios: nao' }
}

Write-Titulo 'Pre-requisitos e banco isolado'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
        $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    } else {
        Write-Host '  ERRO: dotnet nao encontrado no PATH.' -ForegroundColor Red
        exit 1
    }
} else {
    $dotnet = 'dotnet'
}
Write-Host "  dotnet: $dotnet"

if (-not (Test-Path $DbVersionado)) {
    Write-Host "  ERRO: banco versionado nao encontrado em $DbVersionado" -ForegroundColor Red
    exit 1
}

# Hash do banco VERSIONADO antes de comecar
$HashAntes = (Get-FileHash $DbVersionado -Algorithm SHA256).Hash
Write-Host "  controleestoque.db (versionado) SHA256 antes: $HashAntes"

Write-Titulo "Subindo a aplicacao em $BaseUrl (banco isolado: $DbIsolado)"

$env:ASPNETCORE_ENVIRONMENT               = 'Development'
$env:ConnectionStrings__DefaultConnection = "Data Source=$DbIsolado"
$env:ASPNETCORE_URLS                      = $BaseUrl
$env:DOTNET_CLI_TELEMETRY_OPTOUT          = '1'

$appLog = Join-Path $env:TEMP 'qa-authz-app.log'
$app = Start-Process -FilePath $dotnet `
        -ArgumentList 'run','--no-launch-profile','--urls',$BaseUrl `
        -WorkingDirectory $ProjetoDir `
        -RedirectStandardOutput $appLog `
        -RedirectStandardError  "$appLog.err" `
        -WindowStyle Hidden `
        -PassThru

$pronto = $false
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Milliseconds 1000
    if ($app.HasExited) { break }
    try {
        $null = Invoke-RestMethod -Uri "$BaseUrl/Account/Setup" -TimeoutSec 3
        $pronto = $true
        break
    } catch { }
}

if (-not $pronto) {
    Write-Host '  ERRO: a aplicacao nao subiu. Log:' -ForegroundColor Red
    if (Test-Path $appLog) { Get-Content $appLog -Tail 40 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    exit 1
}
Write-Ok "aplicacao no ar em $BaseUrl (PID $($app.Id))"
Say ''
try {
Write-Titulo '1) SETUP: criar o Dono'
$dono = NovaSessao
$sp = Chama $dono 'GET' '/Account/Setup' $null $null $false
Ok 'GET /Account/Setup' 200 $sp
$tk = Token $sp.Body
$b = 'NomeExibicao=Dono%20Teste&Email=dono%40teste.com&Senha=Dono%40123&ConfirmarSenha=Dono%40123'
$cr = Chama $dono 'POST' '/Account/Setup' $b $tk $false
Ok 'POST /Account/Setup (cria o Dono)' 302 $cr
Ok 'GET /Caixa/Index (Dono ja autenticado)' 200 (Chama $dono 'GET' '/Caixa/Index' $null $null $false)
Say ''
Write-Titulo '2) ANONIMO: nenhuma rota de dominio pode responder 200'
$anon = NovaSessao
Ok302Login 'GET /Caixa/Index' (Chama $anon 'GET' '/Caixa/Index' $null $null $false)
Ok302Login 'GET /Home/Index' (Chama $anon 'GET' '/Home/Index' $null $null $false)
Ok302Login 'GET /Caixa/Historico' (Chama $anon 'GET' '/Caixa/Historico' $null $null $false)
Ok302Login 'GET /Caixa/RelatorioDados' (Chama $anon 'GET' '/Caixa/RelatorioDados' $null $null $false)
Ok302Login 'GET /Caixa/ObterStatusAtual' (Chama $anon 'GET' '/Caixa/ObterStatusAtual' $null $null $false)
Ok302Login 'GET /Caixa/ImprimirRelatorioHoje' (Chama $anon 'GET' '/Caixa/ImprimirRelatorioHoje' $null $null $false)
Ok302Login 'GET /Usuarios/Index' (Chama $anon 'GET' '/Usuarios/Index' $null $null $false)
Ok302Login 'GET /Usuarios/Criar' (Chama $anon 'GET' '/Usuarios/Criar' $null $null $false)
Ok302Login 'GET /Home/Novo' (Chama $anon 'GET' '/Home/Novo' $null $null $false)
Ok302Login 'GET /Home/Produto/1' (Chama $anon 'GET' '/Home/Produto/1' $null $null $false)
Ok302Login 'GET /Dashboard' (Chama $anon 'GET' '/Dashboard' $null $null $false)
Ok302Login 'GET /Dashboard/Dia' (Chama $anon 'GET' '/Dashboard/Dia' $null $null $false)
Nao2xx 'POST /Caixa/RegistrarSangria (anonimo)' (Chama $anon 'POST' '/Caixa/RegistrarSangria' 'valor=10.00&obs=anon' $null $true)
Nao2xx 'POST /Usuarios/Criar (anonimo)' (Chama $anon 'POST' '/Usuarios/Criar' 'Nome=X&Email=x%40x.com&Senha=Abc%40123&ConfirmarSenha=Abc%40123&Papel=Dono' $null $true)
Say ''
Write-Titulo '3) DONO: tela nova de usuarios (commit 6)'
$pg = Chama $dono 'GET' '/Usuarios/Criar' $null $null $false
Ok 'GET /Usuarios/Criar (Dono)' 200 $pg
SemTexto '  sem validacao em ingles (data-val-required)' $pg.Body 'field is required'
$bOp = 'Nome=Operador%20Teste&Email=operador%40teste.com&Senha=Operador%40123&ConfirmarSenha=Operador%40123&Papel=Operador'
Ok 'POST /Usuarios/Criar (cria Operador)' 302 (Chama $dono 'POST' '/Usuarios/Criar' $bOp (Token $pg.Body) $false)
$pg = Chama $dono 'GET' '/Usuarios/Criar' $null $null $false
$dup = Chama $dono 'POST' '/Usuarios/Criar' $bOp (Token $pg.Body) $false
Ok 'POST /Usuarios/Criar (e-mail repetido) re-renderiza' 200 $dup
TemTexto '  aviso em portugues' $dup.Body 'existe uma conta com este e-mail'
$pg = Chama $dono 'GET' '/Usuarios/Criar' $null $null $false
$bInv = 'Nome=Invasor&Email=invasor%40teste.com&Senha=Invasor%40123&ConfirmarSenha=Invasor%40123&Papel=SuperAdmin'
$inv = Chama $dono 'POST' '/Usuarios/Criar' $bInv (Token $pg.Body) $false
Nao302 'POST /Usuarios/Criar (papel inventado) NAO cria' $inv
TemTexto '  aviso de papel invalido (acento codificado pelo Razor)' $inv.Body 'Selecione um papel v&#xE1;lido.'
$lst = Chama $dono 'GET' '/Usuarios' $null $null $false
Ok 'GET /Usuarios (listagem)' 200 $lst
TemTexto '  lista o Dono' $lst.Body 'dono@teste.com'
TemTexto '  lista o Operador criado' $lst.Body 'operador@teste.com'
TemTexto '  mostra o papel Operador' $lst.Body '>Operador<'
TemTexto '  mostra o papel Dono' $lst.Body '>Dono<'
SemTexto '  o invasor NAO foi criado' $lst.Body 'invasor@teste.com'
Say ''
Write-Titulo '4) DONO: dinheiro, produto e vendedor'
$pg = Chama $dono 'GET' '/Caixa/Index' $null $null $false
$sup = Chama $dono 'POST' '/Caixa/RegistrarSuprimento' 'valor=100.00&obs=suprimento+do+dono' (Token $pg.Body) $true
Ok 'POST /Caixa/RegistrarSuprimento (Dono) permitido' 200 $sup
$pg = Chama $dono 'GET' '/Caixa/Index' $null $null $false
$sg = Chama $dono 'POST' '/Caixa/RegistrarSangria' 'valor=10.00&obs=sangria+do+dono' (Token $pg.Body) $true
Ok 'POST /Caixa/RegistrarSangria (Dono) permitido' 200 $sg
TemTexto '  sangria gravada' $sg.Body 'true'
$pg = Chama $dono 'GET' '/Home/Novo' $null $null $false
Ok 'POST /Home/Novo (Dono cria produto)' 302 (Chama $dono 'POST' '/Home/Novo' 'nome=PRODUTO%20TESTE&tipo=Prateleira&quantidade=100&preco=1000&codigoBarras=TESTE001' (Token $pg.Body) $false)
$pg = Chama $dono 'GET' '/Caixa/Index' $null $null $false
$cv = Chama $dono 'POST' '/Caixa/CadastrarVendedor' 'nome=VENDEDOR%20TESTE&codigo=CUPOM1' (Token $pg.Body) $true
Ok 'POST /Caixa/CadastrarVendedor (Dono) permitido' 200 $cv
TemTexto '  vendedor criado' $cv.Body 'true'
Ok 'GET /Caixa/ObterStatusAtual (Dono)' 200 (Chama $dono 'GET' '/Caixa/ObterStatusAtual' $null $null $true)
Ok 'GET /Caixa/Historico (Dono)' 200 (Chama $dono 'GET' '/Caixa/Historico' $null $null $false)
Ok 'GET /Usuarios/Criar (Dono) de novo' 200 (Chama $dono 'GET' '/Usuarios/Criar' $null $null $false)
Say '  -- dashboard (ETAPA B, PodeVerFinanceiro) --'
$dash = Chama $dono 'GET' '/Dashboard' $null $null $false
Ok 'GET /Dashboard (Dono)' 200 $dash
TemTexto '  mostra o cartao de vendas de hoje' $dash.Body 'Vendas de hoje'
TemTexto '  mostra o cartao do dinheiro na gaveta' $dash.Body 'Dinheiro na gaveta'
TemTexto '  mostra a serie de 7 dias' $dash.Body 'ltimos 7 dias'
TemTexto '  mostra o seletor de periodo' $dash.Body 'dias='
$dj = Chama $dono 'GET' '/Dashboard/Dia' $null $null $false
Ok 'GET /Dashboard/Dia sem data (redireciona)' 302 $dj
$dj = Chama $dono 'GET' '/Dashboard/Dia?data=nao-e-data' $null $null $false
Ok 'GET /Dashboard/Dia com data invalida (redireciona)' 302 $dj
$dj = Chama $dono 'GET' '/Dashboard/Dia?data=2099-01-01' $null $null $false
Ok 'GET /Dashboard/Dia com data futura (redireciona)' 302 $dj
$dj = Chama $dono 'GET' ('/Dashboard/Dia?data=' + (Get-Date).AddDays(-1).ToString('yyyy-MM-dd')) $null $null $false
Ok 'GET /Dashboard/Dia com data valida' 200 $dj
TemTexto '  mostra o que foi vendido' $dj.Body 'O que foi vendido'
Say ''
Say ''
Write-Titulo '5) OPERADOR: PDV e suprimento passam; o resto e 403'
$op = Login 'operador@teste.com' 'Operador@123'
if ($op.Ok) { $global:p++; Write-Ok 'login do Operador (criado pela tela nova)' } else { $global:f++; Write-Falha 'login do Operador falhou' }
$jar = $op.Jar
Ok 'GET /Caixa/Index (PDV)' 200 (Chama $jar 'GET' '/Caixa/Index' $null $null $true)
Ok 'GET /Home/Index (consulta de estoque)' 200 (Chama $jar 'GET' '/Home/Index' $null $null $true)
$bc = Chama $jar 'GET' '/Caixa/BuscarPorCodigo?codigo=TESTE001' $null $null $true
Ok 'GET /Caixa/BuscarPorCodigo (bipa produto)' 200 $bc
$idProd = ([regex]::Match($bc.Body, '"id":(\d+)')).Groups[1].Value
Say ('          produto encontrado: id=' + $idProd)
Ok 'GET /Home/Produto (detalhe)' 200 (Chama $jar 'GET' "/Home/Produto/$idProd" $null $null $true)
$pg = Chama $jar 'GET' '/Caixa/Index' $null $null $false
$bv = "metodoPagamento=Dinheiro&cpfCliente=&codigoVendedor=CUPOM1&valorRecebido=10&valorTroco=0&itens%5B0%5D.ProdutoId=$idProd&itens%5B0%5D.Quantidade=1&itens%5B0%5D.Preco=10"
$fv = Chama $jar 'POST' '/Caixa/FinalizarVenda' $bv (Token $pg.Body) $true
Ok 'POST /Caixa/FinalizarVenda (Operador VENDEU)' 200 $fv
TemTexto '  resposta da venda = success' $fv.Body '"success":true'
$pg = Chama $jar 'GET' '/Caixa/Index' $null $null $false
$sup2 = Chama $jar 'POST' '/Caixa/RegistrarSuprimento' 'valor=20.00&obs=suprimento+do+operador' (Token $pg.Body) $true
Ok 'POST /Caixa/RegistrarSuprimento (Operador) permitido' 200 $sup2
TemTexto '  suprimento gravado' $sup2.Body '"success":true'
$pg = Chama $jar 'GET' '/Caixa/Index' $null $null $false
Ok 'POST /Caixa/RegistrarSangria (Operador, AJAX) -> 403' 403 (Chama $jar 'POST' '/Caixa/RegistrarSangria' 'valor=10.00&obs=operador+tentou' (Token $pg.Body) $true)
$pg = Chama $jar 'GET' '/Caixa/Index' $null $null $false
Ok302Negado 'POST /Caixa/RegistrarSangria (Operador, navegacao)' (Chama $jar 'POST' '/Caixa/RegistrarSangria' 'valor=10.00&obs=operador+tentou2' (Token $pg.Body) $false)
Say '  -- relatorios (PodeVerFinanceiro) --'
Ok 'GET /Caixa/Historico' 403 (Chama $jar 'GET' '/Caixa/Historico' $null $null $true)
Ok 'GET /Caixa/RelatorioDados' 403 (Chama $jar 'GET' '/Caixa/RelatorioDados' $null $null $true)
Ok 'GET /Caixa/ObterResumo' 403 (Chama $jar 'GET' '/Caixa/ObterResumo' $null $null $true)
Ok 'GET /Caixa/ImprimirRelatorioHoje' 403 (Chama $jar 'GET' '/Caixa/ImprimirRelatorioHoje' $null $null $true)
Ok 'GET /Caixa/VerVendasVendedor' 403 (Chama $jar 'GET' '/Caixa/VerVendasVendedor?codigo=CUPOM1' $null $null $true)
Say '  -- fechamento (PodeFecharCaixa) --'
Ok 'GET /Caixa/ObterStatusAtual' 403 (Chama $jar 'GET' '/Caixa/ObterStatusAtual' $null $null $true)
$pg = Chama $jar 'GET' '/Caixa/Index' $null $null $false
Ok 'POST /Caixa/ZerarCaixaParaProximoDia' 403 (Chama $jar 'POST' '/Caixa/ZerarCaixaParaProximoDia' '' (Token $pg.Body) $true)
Say '  -- gerenciar estoque (PodeGerenciarEstoque) --'
$pg = Chama $jar 'GET' '/Caixa/Index' $null $null $false
Ok 'POST /Home/Novo' 403 (Chama $jar 'POST' '/Home/Novo' 'nome=HACK&tipo=Prateleira&quantidade=1&preco=100&codigoBarras=HACK1' (Token $pg.Body) $true)
Ok 'POST /Home/Movimentar' 403 (Chama $jar 'POST' '/Home/Movimentar?id=1&valor=5&tipo=Entrada' '' (Token $pg.Body) $true)
Ok 'POST /Home/Editar' 403 (Chama $jar 'POST' '/Home/Editar?id=1&nome=X&tipo=Prateleira&quantidade=1&preco=100' '' (Token $pg.Body) $true)
Ok 'POST /Home/AjustarEstoque' 403 (Chama $jar 'POST' '/Home/AjustarEstoque?id=1&quantidadeAjuste=5&operacao=Somar' '' (Token $pg.Body) $true)
Ok 'POST /Home/Excluir/1' 403 (Chama $jar 'POST' '/Home/Excluir/1' '' (Token $pg.Body) $true)
Say '  -- gerenciar vendedores (PodeGerenciarVendedores) --'
Ok 'POST /Caixa/CadastrarVendedor' 403 (Chama $jar 'POST' '/Caixa/CadastrarVendedor' 'nome=HACK&codigo=HACK' (Token $pg.Body) $true)
Ok 'POST /Caixa/ExcluirVendedor' 403 (Chama $jar 'POST' '/Caixa/ExcluirVendedor?codigo=CUPOM1' '' (Token $pg.Body) $true)
Say '  -- usuarios (commit 6, PodeGerenciarUsuarios) --'
Ok 'GET /Usuarios' 403 (Chama $jar 'GET' '/Usuarios' $null $null $true)
Ok 'GET /Usuarios/Criar' 403 (Chama $jar 'GET' '/Usuarios/Criar' $null $null $true)
Ok 'POST /Usuarios/Criar' 403 (Chama $jar 'POST' '/Usuarios/Criar' 'Nome=HACK&Email=hack%40teste.com&Senha=Hack%40123&ConfirmarSenha=Hack%40123&Papel=Dono' (Token $pg.Body) $true)
Ok302Negado 'GET /Usuarios (navegacao do Operador)' (Chama $jar 'GET' '/Usuarios' $null $null $false)
Say '  -- dashboard (ETAPA B, PodeVerFinanceiro) --'
# O painel mostra faturamento e saldo da gaveta: nao e do Operador.
Ok 'GET /Dashboard' 403 (Chama $jar 'GET' '/Dashboard' $null $null $true)
Ok302Negado 'GET /Dashboard (navegacao do Operador)' (Chama $jar 'GET' '/Dashboard' $null $null $false)
Ok 'GET /Dashboard/Dia' 403 (Chama $jar 'GET' '/Dashboard/Dia' $null $null $true)
Ok302Negado 'GET /Dashboard/Dia (navegacao do Operador)' (Chama $jar 'GET' '/Dashboard/Dia' $null $null $false)
Say ''

# =============================================================================
# 6) MENU POR PAPEL (commit 4) - o que SAI no HTML de cada perfil.
#    O menu nao e a seguranca (quem barra e o [Authorize] da action, ja testado
#    nos blocos 2 a 5): aqui se confere o que a pessoa VE - que e o que evita
#    oferecer ao operador um botao que so pode terminar em 403.
#    Estes testes leem o MESMO HTML que o navegador recebe (inclusive a navbar
#    e o modal Alt+M), entao valem como verificacao visual automatizada.
# =============================================================================
Write-Titulo '6) MENU POR PAPEL (commit 4) - HTML renderizado'

$hDono = (Chama $dono 'GET' '/Caixa/Index' $null $null $false).Body
Write-Host '  -- Dono: menu completo --'
TemTexto 'Dono: navbar mostra o nome' $hDono '>Dono Teste</b>'
TemTexto 'Dono: navbar mostra o papel' $hDono '>Dono</span>'
TemTexto 'Dono: link /Usuarios na navbar' $hDono 'href="/Usuarios"'
TemTexto 'Dono: link do Historico na navbar' $hDono 'href="/Caixa/Historico"'
TemTexto 'Dono: item 1 (SANGRIA) no menu Alt+M' $hDono 'data-key="1"'
TemTexto 'Dono: item 2 (SUPRIMENTO) no menu Alt+M' $hDono 'data-key="2"'
TemTexto 'Dono: item 3 (CAIXA) no menu Alt+M' $hDono 'data-key="3"'
TemTexto 'Dono: item 4 (FECHAMENTO) no menu Alt+M' $hDono 'data-key="4"'
TemTexto 'Dono: item 5 (RELATORIO) no menu Alt+M' $hDono 'data-key="5"'
TemTexto 'Dono: item 6 (REIMPRIMIR) no menu Alt+M' $hDono 'data-key="6"'
TemTexto 'Dono: item 7 (VENDEDORES) no menu Alt+M' $hDono 'data-key="7"'
TemTexto 'Dono: rotulo MENU ADMINISTRATIVO' $hDono 'MENU ADMINISTRATIVO'
TemTexto 'Dono: botao Sair na navbar' $hDono '>Sair</button>'
TemTexto 'Dono: JS libera a gestao de vendedores' $hDono 'vendedores: true'

$hOp = (Chama $jar 'GET' '/Caixa/Index' $null $null $false).Body
Write-Host '  -- Operador: so o que ele pode usar --'
TemTexto 'Operador: navbar mostra o nome' $hOp '>Operador Teste</b>'
TemTexto 'Operador: navbar mostra o papel' $hOp '>Operador</span>'
TemTexto 'Operador: botao Sair na navbar' $hOp '>Sair</button>'
TemTexto 'Operador: item 2 (SUPRIMENTO) no menu Alt+M' $hOp 'data-key="2"'
TemTexto 'Operador: item 6 (REIMPRIMIR) no menu Alt+M' $hOp 'data-key="6"'
TemTexto 'Operador: item 7 (VENDEDORES - consulta) no menu Alt+M' $hOp 'data-key="7"'
SemTexto 'Operador: NAO tem item 1 (SANGRIA)' $hOp 'data-key="1"'
SemTexto 'Operador: NAO tem item 3 (CAIXA)' $hOp 'data-key="3"'
SemTexto 'Operador: NAO tem item 4 (FECHAMENTO)' $hOp 'data-key="4"'
SemTexto 'Operador: NAO tem item 5 (RELATORIO)' $hOp 'data-key="5"'
SemTexto 'Operador: NAO tem link /Usuarios' $hOp 'href="/Usuarios"'
SemTexto 'Operador: NAO tem link do Historico' $hOp 'href="/Caixa/Historico"'
SemTexto 'Operador: NAO ve o rotulo MENU ADMINISTRATIVO' $hOp 'MENU ADMINISTRATIVO'
TemTexto 'Operador: ve MENU DO CAIXA' $hOp 'MENU DO CAIXA'
TemTexto 'Operador: JS diz que opera o PDV' $hOp 'pdv: true'
TemTexto 'Operador: JS diz que NAO ve o financeiro' $hOp 'financeiro: false'
TemTexto 'Operador: JS diz que NAO gerencia vendedores' $hOp 'vendedores: false'

$hAnon = (Chama $anon 'GET' '/Account/Login' $null $null $false).Body
Write-Host '  -- Anonimo: nenhum menu --'
SemTexto 'Anonimo: sem menu Alt+M' $hAnon 'id="modalAtalhos"'
SemTexto 'Anonimo: sem link /Usuarios' $hAnon 'href="/Usuarios"'
SemTexto 'Anonimo: sem o rotulo MENU ADMINISTRATIVO' $hAnon 'MENU ADMINISTRATIVO'
SemTexto 'Anonimo: sem botao Sair' $hAnon '>Sair</button>'
SemTexto 'Anonimo: sem nome/papel na navbar' $hAnon 'class="badge bg-secondary"'
SemTexto 'Anonimo: sem link de navegacao para o Caixa' $hAnon 'href="/Caixa"'

# Mesma regra do menu vale para os botoes dentro das telas: a tela de Estoque e
# de consulta (PodeOperarPDV), mas Novo/Editar/Excluir/AjustarEstoque pedem
# PodeGerenciarEstoque. Se o Operador ve o botao, o maximo que ele consegue e um
# 403 - entao o botao nao vai nem para o HTML dele.
Write-Host '  -- Tela de Estoque: botoes de Dono apenas para o Dono --'
$eDono = (Chama $dono 'GET' '/Home/Index' $null $null $false).Body
TemTexto 'Dono: botao de novo produto' $eDono 'btn-novo'
TemTexto 'Dono: botao editar na tabela' $eDono 'btn-editar'
TemTexto 'Dono: botao excluir na tabela' $eDono 'btn-excluir'
$pDono = (Chama $dono 'GET' '/Home/Produto/1' $null $null $false).Body
TemTexto 'Dono: formulario de ajuste de estoque' $pDono 'QUANTIDADE PARA AJUSTE'

$eOp = (Chama $jar 'GET' '/Home/Index' $null $null $false).Body
SemTexto 'Operador: sem botao de novo produto' $eOp 'btn-novo'
SemTexto 'Operador: sem botao editar' $eOp 'btn-editar'
SemTexto 'Operador: sem botao excluir' $eOp 'btn-excluir'
SemTexto 'Operador: sem coluna de acoes na tabela' $eOp 'class="acoes"'
$pOp = (Chama $jar 'GET' '/Home/Produto/1' $null $null $false).Body
SemTexto 'Operador: sem formulario de ajuste' $pOp 'QUANTIDADE PARA AJUSTE'
SemTexto 'Operador: sem botao adicionar/remover' $pOp 'btn-ajuste'
TemTexto 'Operador: continua podendo voltar para o estoque' $pOp 'btn-voltar-borda'
TemTexto 'Operador: ve o aviso de modo consulta' $pOp 'modo consulta'

# Versao "visual" (texto) do que cada perfil recebe - so para revisao humana.
Mostrar-Menu 'DONO: o que o navegador mostra' $hDono
Mostrar-Menu 'OPERADOR: o que o navegador mostra' $hOp
Mostrar-Menu 'ANONIMO: o que o navegador mostra' $hAnon
Say ''
Write-Titulo 'RESULTADO FINAL'
Write-Host "  checagens aprovadas: $global:p" -ForegroundColor Green
Write-Host "  checagens com falha:  $global:f" -ForegroundColor Red
if ($global:f -eq 0) {
    Write-Ok 'TESTE HTTP DE AUTORIZACAO DOS 3 PERFIS: OK'
} else {
    Write-Falha 'TESTE HTTP DE AUTORIZACAO DOS 3 PERFIS: FALHOU'
    $codigoSaida = 1
}
}
finally {
    if ($ManterApp) {
        Write-Host "  -ManterApp: a aplicacao segue no ar em $BaseUrl (PID $($app.Id))" -ForegroundColor Yellow
    } elseif ($app -and -not $app.HasExited) {
        & taskkill /PID $app.Id /T /F 2>$null | Out-Null
        Write-Host "  aplicacao encerrada (PID $($app.Id))"
    }
}

# =============================================================================
# LIMPEZA + VERIFICACAO DE QUE O BANCO VERSIONADO NAO FOI TOCADO
# =============================================================================
Write-Titulo 'Verificando o banco VERSIONADO (controleestoque.db)'

$HashDepois = (Get-FileHash $DbVersionado -Algorithm SHA256).Hash
Write-Host "  controleestoque.db (versionado) SHA256 antes : $HashAntes"
Write-Host "  controleestoque.db (versionado) SHA256 depois: $HashDepois"

if ($HashAntes -eq $HashDepois) {
    Write-Ok 'BANCO DE PRODUCAO INTACTO - hash identico antes e depois'
} else {
    Write-Falha 'O BANCO DE PRODUCAO FOI ALTERADO! Investigue imediatamente.'
    $codigoSaida = 2
}

if (-not $ManterApp) {
    foreach ($sufixo in @('', '-shm', '-wal')) {
        $arq = "$DbIsolado$sufixo"
        if (Test-Path $arq) {
            try { Remove-Item $arq -Force -ErrorAction Stop }
            catch { Write-Host "  (nao foi possivel remover $arq : $($_.Exception.Message))" -ForegroundColor DarkGray }
        }
    }
    Write-Host "  banco isolado removido: $DbIsolado"
}

Write-Titulo 'FIM'
Write-Host '  Qualquer [FALHA] acima e um defeito de autorizacao (BUG-13).' -ForegroundColor Yellow
Write-Host '  Ver qa\RELATORIO-QA.md e rode "dotnet test" em tests.QA para o detalhamento.' -ForegroundColor Yellow

exit $codigoSaida