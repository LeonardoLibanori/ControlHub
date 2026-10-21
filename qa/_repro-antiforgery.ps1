<#
.SYNOPSIS
    Reproduz o 400 de antiforgery no POST /Usuarios/Criar e imprime a excecao
    exata (log Microsoft.AspNetCore.Antiforgery em Debug).

.DESCRIPTION
    Sobe a app em banco isolado, cria o Dono via /Account/Setup e tenta criar
    um Operador de DUAS formas:
      1) token no header X-CSRF-TOKEN
      2) token no campo hidden __RequestVerificationToken
    Imprime codigo + Location de cada uma e o log do servidor.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\qa\_repro-antiforgery.ps1
#>
[CmdletBinding()]
param(
    [int]$Porta = 5399
)
$ErrorActionPreference = 'Stop'

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjetoDir = Split-Path -Parent $ScriptDir
. (Join-Path $ScriptDir 'senha-teste.ps1')
$SenhaDono = Get-SenhaDono
$SenhaOper = Get-SenhaOperador
$DbIsolado = Join-Path $env:TEMP ('qa-repro-' + [Guid]::NewGuid().ToString('N') + '.db')
$BaseUrl   = "http://127.0.0.1:$Porta"

function Say([string]$t) { Write-Host $t }

function Chama($jar, $m, $u, $b, $tok, $modo) {
    $q = [System.Net.HttpWebRequest]::Create("$BaseUrl$u")
    $q.Method = $m; $q.AllowAutoRedirect = $false; $q.Timeout = 30000; $q.CookieContainer = $jar
    $q.Accept = 'text/html,application/xhtml+xml'
    if ($b) {
        $q.ContentType = 'application/x-www-form-urlencoded'
        $by = [System.Text.Encoding]::UTF8.GetBytes($b); $q.ContentLength = $by.Length
        $st = $q.GetRequestStream(); $st.Write($by, 0, $by.Length); $st.Close()
    }
    if ($modo -eq 'header' -and $tok) { $q.Headers.Add('X-CSRF-TOKEN', $tok) }
    try { $rs = $q.GetResponse() }
    catch [System.Net.WebException] { $rs = $_.Exception.Response; if (-not $rs) { return [pscustomobject]@{ Code = -1; Body = ''; Loc = '' } } }
    $code = [int]$rs.StatusCode; $loc = ''
    try { if ($rs.Headers['Location']) { $loc = [string]$rs.Headers['Location'] } } catch {}
    $txt = ''
    try { $sr = New-Object System.IO.StreamReader($rs.GetResponseStream(), [System.Text.Encoding]::UTF8); $txt = $sr.ReadToEnd(); $sr.Close() } catch {}
    $rs.Close()
    return [pscustomobject]@{ Code = $code; Body = $txt; Loc = $loc }
}

function Token($html) {
    $m = [regex]::Match($html, '<meta\s+name="csrf-token"\s+content="([^"]*)"')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

# ---- derruba a porta, se ocupada -------------------------------------------
Get-NetTCPConnection -LocalPort $Porta -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { try { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue } catch {} }
Start-Sleep -Milliseconds 800

$env:ASPNETCORE_ENVIRONMENT               = 'Development'
$env:ConnectionStrings__DefaultConnection = "Data Source=$DbIsolado"
$env:ASPNETCORE_URLS                      = $BaseUrl
$env:DOTNET_CLI_TELEMETRY_OPTOUT          = '1'
# LOG DE DEBUG DO ANTIFORGERY: e o que revela a excecao real do 400.
# A CATEGORIA tem PONTO (Microsoft.AspNetCore.Antiforgery). O "__" do env var
# e so o separador de hierarquia da configuracao (Logging__LogLevel__<categoria>);
# trocar o ponto por "_" cria uma categoria inexistente e o log some.
$env:Logging__LogLevel__Default                          = 'Information'
# CATEGORIA com PONTO. "Logging__LogLevel__<categoria>" - o "__" separa os
# NIVEL de configuracao; dentro da categoria o separador e ".". Escrever
# "Microsoft_AspNetCore_Antiforgery" cria uma categoria que nao existe e o
# log some (foi o que escondeu a causa do 400 ate agora).
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Antiforgery',      'Information', 'Process')
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Mvc',              'Information', 'Process')
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Authorization',   'Information', 'Process')
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Authentication',  'Information', 'Process')

$appLog = Join-Path $env:TEMP 'qa-repro-app.log'
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

try {
    $dono = New-Object System.Net.CookieContainer

    # ---- Dono via /Account/Setup (token no campo hidden) -------------------
    $sp = Chama $dono 'GET' '/Account/Setup' $null $null 'hidden'
    $tk = Token $sp.Body
    $bD = 'NomeExibicao=Dono%20Teste&Email=dono%40teste.com&Senha=' + [uri]::EscapeDataString($SenhaDono) + '&ConfirmarSenha=' + [uri]::EscapeDataString($SenhaDono) + '&__RequestVerificationToken=' + [uri]::EscapeDataString($tk)
    $cr = Chama $dono 'POST' '/Account/Setup' $bD $null 'hidden'
    Say "POST /Account/Setup (hidden) -> $($cr.Code) $($cr.Loc)"
    if ($cr.Code -ne 302) { Say 'ERRO: nao criou o Dono'; throw 'abortado' }

    # ---- Token da tela /Usuarios/Criar ------------------------------------
    $pg = Chama $dono 'GET' '/Usuarios/Criar' $null $null 'hidden'
    $tk2 = Token $pg.Body
    Say "token meta /Usuarios/Criar len=$($tk2.Length)"
    $cookies = ($dono.GetCookies([uri]$BaseUrl) | ForEach-Object { $_.Name }) -join ', '
    Say "cookies no jar: $cookies"

    $campos = 'Nome=Operador%20Teste&Email=operador%40teste.com&Senha=' + [uri]::EscapeDataString($SenhaOper) + '&ConfirmarSenha=' + [uri]::EscapeDataString($SenhaOper) + '&Papel=Operador'

    # ---- CASO 1: token no HEADER ------------------------------------------
    $c1 = Chama $dono 'POST' '/Usuarios/Criar' $campos $tk2 'header'
    Say "CASO 1 header X-CSRF-TOKEN -> $($c1.Code) $($c1.Loc)"
    if ($c1.Body) { Say ("  body1: " + ($c1.Body -replace '(?s)<[^>]+>',' ' -replace '\s+',' ').Trim().Substring(0,[Math]::Min(300,($c1.Body -replace '(?s)<[^>]+>',' ' -replace '\s+',' ').Trim().Length))) }

    # ---- CASO 2: token no campo hidden ------------------------------------
    $b2 = $campos + '&__RequestVerificationToken=' + [uri]::EscapeDataString($tk2)
    $c2 = Chama $dono 'POST' '/Usuarios/Criar' $b2 $null 'hidden'
    Say "CASO 2 campo hidden          -> $($c2.Code) $($c2.Loc)"
    if ($c2.Body) { Say ("  body2: " + ($c2.Body -replace '(?s)<[^>]+>',' ' -replace '\s+',' ').Trim().Substring(0,[Math]::Min(300,($c2.Body -replace '(?s)<[^>]+>',' ' -replace '\s+',' ').Trim().Length))) }

    # ---- trecho do log de antiforgery --------------------------------------
    Say ''
    Say '=== LOG (Antiforgery) ==='
    if (Test-Path $appLog) {
        Get-Content $appLog | Where-Object { $_ -match 'ntiforgery' } |
            Select-Object -Last 60 | ForEach-Object { Say "  $_" }
    }
    Say ''
    Say '=== ULTIMAS LINHAS DO LOG ==='
    if (Test-Path $appLog) { Get-Content $appLog -Tail 30 | ForEach-Object { Say "  $_" } }
}
finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
}
