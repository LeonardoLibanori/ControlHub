<#
.SYNOPSIS
    QA por HTTP real - os assets locais da aplicacao chegam inteiros ao browser.

.DESCRIPTION
    Cobre o BUG-27: com o cabecalho "Accept-Encoding: gzip, br" (que TODO browser
    envia), o antigo app.MapStaticAssets() respondia 200 com Content-Length 0 e
    sem Content-Type. O browser aceitava a folha de estilo vazia e a pagina ficava
    sem estilo nenhum - botao cru e o modal de Operacao vazando no meio do login.

    Para cada asset local sao feitas DUAS requisicoes:

      1. COM    "Accept-Encoding: gzip, br"  -> o caminho do browser de verdade
      2. SEM    o cabecalho                   -> o caminho "limpo"

    As duas tem de responder HTTP 200, Content-Length > 0 e o Content-Type
    esperado. Qualquer asset que falhe faz o script sair com codigo de erro.

    NAO Sobe a aplicacao de proposito: este teste e somente de leitura de arquivo
    estatico e o http-authz.ps1 ja sobe o app contra um banco ISOLADO. Como o
    controleestoque.db e VERSIONADO, este script nao pode ser o responsavel por
    abrir o banco de producao. Com a aplicacao ja rodando:

        powershell -ExecutionPolicy Bypass -File .\http-assets.ps1
        powershell -ExecutionPolicy Bypass -File .\http-assets.ps1 -BaseUrl http://localhost:5299

.NOTES
    Requer ExecutionPolicy Bypass.
    Codigo de saida: 0 = tudo certo; 1 = algum asset falhou; 2 = app nao alcancavel.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\http-assets.ps1
    powershell -ExecutionPolicy Bypass -File .\http-assets.ps1 -BaseUrl http://localhost:5236
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5236'
)

$ErrorActionPreference = 'Continue'

$BaseUrl = $BaseUrl.TrimEnd('/')

# Os 6 assets locais que a interface depende: sem qualquer um deles a pagina abre
# cru. Content-Type esperado e conferido por PREFIXO, porque o Kestrel pode
# responder "text/css" ou "text/css; charset=utf-8" conforme o arquivo.
$Assets = @(
    @{ Caminho = '/css/corporate-theme.css';                 Tipo = 'text/css';       Nome = 'CSS do tema corporativo' }
    @{ Caminho = '/lib/bootstrap/dist/css/bootstrap.min.css'; Tipo = 'text/css';       Nome = 'Bootstrap (css)' }
    @{ Caminho = '/js/admin-caixa.js';                       Tipo = 'text/javascript'; Nome = 'JS do admin do caixa' }
    @{ Caminho = '/js/site.js';                              Tipo = 'text/javascript'; Nome = 'JS geral do site' }
    @{ Caminho = '/lib/jquery/dist/jquery.min.js';           Tipo = 'text/javascript'; Nome = 'jQuery' }
    @{ Caminho = '/favicon.ico';                             Tipo = 'image/x-icon';    Nome = 'Favicon' }
)

$global:p = 0
$global:f = 0

function Write-Titulo([string]$t) {
    Write-Host ''
    Write-Host "=== $t ===" -ForegroundColor Cyan
}
function Write-Ok([string]$t) {
    $global:p++
    Write-Host "  [OK]    $t" -ForegroundColor Green
}
function Write-Falha([string]$t) {
    $global:f++
    Write-Host "  [FALHA] $t" -ForegroundColor Red
}
# Faz UMA requisicao e devolve um PSCustomObject com o que o servidor respondeu.
# Nao usamos Invoke-WebRequest de proposito: ele descompacta sozinho e esconderia
# justamente o defeito que este teste existe para pegar (corpo vazio).
function Invoke-Asset([string]$caminho, [bool]$comAcceptEncoding) {
    $req = [System.Net.HttpWebRequest]::Create("$BaseUrl$caminho")
    $req.Method = 'GET'
    $req.AllowAutoRedirect = $false
    $req.Timeout = 30000
    $req.UserAgent = 'qa-http-assets/1.0'
    if ($comAcceptEncoding) {
        $req.Headers.Add('Accept-Encoding', 'gzip, br')
    }

    try {
        $resp = $req.GetResponse()
        $r = [PSCustomObject]@{
            Status        = [int]$resp.StatusCode
            ContentLength = [int64]$resp.ContentLength
            ContentType   = [string]$resp.ContentType
            Erro          = $null
        }
        $resp.Close()
        return $r
    }
    catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp) {
            $r = [PSCustomObject]@{
                Status        = [int]$resp.StatusCode
                ContentLength = [int64]$resp.ContentLength
                ContentType   = [string]$resp.ContentType
                Erro          = $_.Exception.Message
            }
            $resp.Close()
            return $r
        }
        return [PSCustomObject]@{ Status = 0; ContentLength = 0; ContentType = ''; Erro = $_.Exception.Message }
    }
}

# ---------------------------------------------------------------------------
Write-Host "QA de assets locais (BUG-27) em $BaseUrl" -ForegroundColor White

# 0) a aplicacao tem de estar no ar: sem isso nao da para separar
#    "asset quebrado" de "app parado". Usamos GET em "/" (o HEAD nao e mapeado
#    pelas actions e o MVC responderia 405, que pareceria app quebrado).
Write-Titulo '0) aplicacao no ar'
try {
    $ping = [System.Net.HttpWebRequest]::Create("$BaseUrl/")
    $ping.Method = 'GET'; $ping.Timeout = 15000; $ping.AllowAutoRedirect = $false
    $respPing = $ping.GetResponse()
    $statusPing = [int]$respPing.StatusCode
    $respPing.Close()
    Write-Ok "aplicacao respondeu em $BaseUrl (HTTP $statusPing)"
}
catch {
    Write-Host ''
    Write-Host "  [ERRO] Nao consegui alcancar $BaseUrl : $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '         Suba a aplicacao (dotnet run) e rode este script de novo.' -ForegroundColor Red
    exit 2
}

foreach ($asset in $Assets) {
    Write-Titulo "$($asset.Nome) -> $($asset.Caminho)"

    foreach ($modo in @($true, $false)) {
        $rotulo = if ($modo) { 'COM Accept-Encoding: gzip, br' } else { 'SEM Accept-Encoding' }
        $r = Invoke-Asset $asset.Caminho $modo

        if ($r.Status -ne 200) {
            Write-Falha "$rotulo -> HTTP $($r.Status) $($r.Erro)"
            continue
        }
        if ($r.ContentLength -le 0) {
            # Esta e a assinatura exata do BUG-27: 200 mas com corpo vazio, e sem
            # Content-Type. O browser descarta a folha de estilo e a pagina fica crua.
            Write-Falha "$rotulo -> HTTP 200 mas Content-Length 0 (corpo vazio) e Content-Type [$($r.ContentType)] - assinatura do BUG-27"
            continue
        }
        if ([string]::IsNullOrWhiteSpace($r.ContentType)) {
            Write-Falha "$rotulo -> HTTP 200, $($r.ContentLength) bytes, mas SEM Content-Type"
            continue
        }
        if (-not $r.ContentType.StartsWith($asset.Tipo, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Falha "$rotulo -> Content-Type [$($r.ContentType)] diferente do esperado [$($asset.Tipo)]"
            continue
        }

        Write-Ok "$rotulo -> HTTP 200, $($r.ContentLength) bytes, $($r.ContentType)"
    }
}

Write-Titulo 'Resumo'
Write-Host "  assets verificados : $($Assets.Count) x 2 requisicoes (com e sem Accept-Encoding)"
Write-Host "  OK    : $global:p" -ForegroundColor Green
Write-Host "  FALHA : $global:f" -ForegroundColor Red

if ($global:f -gt 0) {
    Write-Host ''
    Write-Host 'FALHOU: algum asset local nao chegou inteiro no browser.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'OK: todos os assets locais responderam 200 com corpo e Content-Type correto.' -ForegroundColor Green
exit 0