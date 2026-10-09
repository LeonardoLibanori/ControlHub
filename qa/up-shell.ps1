<#
.SYNOPSIS
    Sobe a instancia de teste do shell (AJUSTE 2) na porta 5311 contra um banco
    ISOLADO em %TEMP% e cria o Dono (via /Account/Setup) e um Operador (via
    /Usuarios/Criar). NAO toca no controleestoque.db versionado.

.DESCRIPTION
    Reaproveita o padrao de qa/http-authz.ps1 (Chama/Token/Login) para provisinar
    os dois perfis. Deixa a aplicacao NO AR e imprime as credenciais de teste.
    Usado pelo diagnostico (qa/_diag2.mjs) e pela guarda (qa/nav-smoke.mjs).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\qa\up-shell.ps1
    powershell -ExecutionPolicy Bypass -File .\qa\up-shell.ps1 -Porta 5311 -ManterApp
#>
[CmdletBinding()]
param(
    [int]$Porta = 5311,
    [switch]$ManterApp
)
$ErrorActionPreference = 'Stop'

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjetoDir = Split-Path -Parent $ScriptDir
$DbIsolado  = Join-Path $env:TEMP ('qa-shell-' + [Guid]::NewGuid().ToString('N') + '.db')
$BaseUrl    = "http://127.0.0.1:$Porta"

function Say([string]$t) { Write-Host $t }
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

# ---- libera a porta se estiver ocupada --------------------------------------
Get-NetTCPConnection -LocalPort $Porta -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { try { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue } catch {} }
Start-Sleep -Milliseconds 800

$env:ASPNETCORE_ENVIRONMENT               = 'Development'
$env:ConnectionStrings__DefaultConnection = "Data Source=$DbIsolado"
$env:ASPNETCORE_URLS                      = $BaseUrl
$env:DOTNET_CLI_TELEMETRY_OPTOUT          = '1'

$appLog = Join-Path $env:TEMP 'qa-shell-app.log'
if (Test-Path $appLog) { Remove-Item $appLog -Force }
Say "subindo a aplicacao em $BaseUrl (db isolado: $DbIsolado)"
$app = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run','--no-launch-profile','--urls',$BaseUrl `
        -WorkingDirectory $ProjetoDir `
        -RedirectStandardOutput $appLog `
        -RedirectStandardError "$appLog.err" `
        -WindowStyle Hidden -PassThru

$pronto = $false
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Milliseconds 1000
    if ($app.HasExited) { break }
    try { $null = Invoke-RestMethod -Uri "$BaseUrl/Account/Setup" -TimeoutSec 3; $pronto = $true; break } catch {}
}
if (-not $pronto) {
    Say 'ERRO: a aplicacao nao subiu. Log:'
    if (Test-Path $appLog) { Get-Content $appLog -Tail 40 | ForEach-Object { Say "  $_" } }
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    exit 1
}
Say "aplicacao no ar (PID $($app.Id))"

# ---- cria o Dono (Setup) e um Operador (/Usuarios/Criar) --------------------
$dono = NovaSessao
$sp = Chama $dono 'GET' '/Account/Setup' $null $null $false
if ($sp.Code -ne 200) { Say "ERRO: GET /Account/Setup -> $($sp.Code)"; exit 1 }
$tk = Token $sp.Body
$bD = 'NomeExibicao=Dono%20Teste&Email=dono%40teste.com&Senha=Dono%40123&ConfirmarSenha=Dono%40123'
$cr = Chama $dono 'POST' '/Account/Setup' $bD $tk $false
if ($cr.Code -ne 302) { Say "ERRO: criar Dono -> $($cr.Code)"; exit 1 }

$pg = Chama $dono 'GET' '/Usuarios/Criar' $null $null $false
$tk2 = Token $pg.Body
$bO = 'Nome=Operador%20Teste&Email=operador%40teste.com&Senha=Operador%40123&ConfirmarSenha=Operador%40123&Papel=Operador'
$co = Chama $dono 'POST' '/Usuarios/Criar' $bO $tk2 $false
if ($co.Code -ne 302) { Say "ERRO: criar Operador -> $($co.Code)"; exit 1 }

Say ''
Say 'CREDENCIAIS DE TESTE (banco isolado, temporario):'
Say "  base .......: $BaseUrl"
Say "  dono .......: dono@teste.com / ***REMOVIDO***"
Say "  operador ...: operador@teste.com / Operador@123"
Say "  db .........: $DbIsolado"
Say "  app PID ....: $($app.Id)"

# grava um marcador para o passo de limpeza
[IO.File]::WriteAllText((Join-Path $env:TEMP 'qa-shell-app.pid'), [string]$app.Id)
[IO.File]::WriteAllText((Join-Path $env:TEMP 'qa-shell-app.db'), $DbIsolado)

if ($ManterApp) { exit 0 }
