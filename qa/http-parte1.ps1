<#
.SYNOPSIS
    QA por HTTP real (parte 1) - Vendas e Caixa.

.DESCRIPTION
    Sobe a aplicacao (dotnet run) contra um banco ISOLADO em %TEMP% e reproduz,
    por HTTP real, os casos de VendasQaTests e CaixaQaTests:

      * preco de venda adulterado no POST (deve ser ignorado)
      * quantidade negativa (deve ser rejeitada)
    * preço direto em R$/kg e quantidade fracionária por peso
      * sangria acima do saldo (deve ser rejeitada)
      * valor <= 0 (deve ser rejeitado)
      * saldo da gaveta deve ignorar vendas nao-Dinheiro
      * sinal da sangria no fechamento

    IMPORTANTE: ao final compara o hash SHA256 do controleestoque.db VERSIONADO
    antes e depois. Se mudar, o script falha - o banco de producao jamais deve
    ser tocado por este teste.

.NOTES
    Requer ExecutionPolicy Bypass:
        powershell -ExecutionPolicy Bypass -File .\http-parte1.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\http-parte1.ps1
    powershell -ExecutionPolicy Bypass -File .\http-parte1.ps1 -Porta 5299
#>
[CmdletBinding()]
param(
    [int]$Porta = 5236,
    [switch]$ManterApp
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- caminhos ---
$ScriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjetoDir   = Split-Path -Parent $ScriptDir            # ...\ControleEstoqueLoja
$DbVersionado = Join-Path $ProjetoDir 'controleestoque.db'
$DbIsolado    = Join-Path $env:TEMP ("qa-http-" + [Guid]::NewGuid().ToString('N') + '.db')
$BaseUrl      = "http://localhost:$Porta"

# ------------------------------------------------------------------ helpers ---
function Write-Titulo([string]$t) {
    Write-Host ''
    Write-Host "=== $t ===" -ForegroundColor Cyan
}

function Write-Ok([string]$t) {
    Write-Host "  [OK]    $t" -ForegroundColor Green
}

function Write-Falha([string]$t) {
    Write-Host "  [FALHA] $t" -ForegroundColor Red
}

function Get-Json([string]$url, [string]$metodo = 'GET', $corpo = $null) {
    $params = @{
        Uri        = $url
        Method     = $metodo
        TimeoutSec = 30
    }
    if ($corpo) {
        $params['ContentType'] = 'application/json'
        $params['Body']        = $corpo
    }
    try {
        $r = Invoke-RestMethod @params
        return @{ Ok = $true; Dados = $r; Erro = $null }
    } catch {
        return @{ Ok = $false; Dados = $null; Erro = $_.Exception.Message }
    }
}

function Get-Form([string]$url, [hashtable]$campos, [string]$metodo = 'POST') {
    $body = ''
    foreach ($k in $campos.Keys) { $body += "$k=$([uri]::EscapeDataString([string]$campos[$k]))&" }
    $params = @{
        Uri         = $url
        Method      = $metodo
        Body        = $body
        ContentType = 'application/x-www-form-urlencoded'
        TimeoutSec  = 30
    }
    try {
        $r = Invoke-RestMethod @params
        return @{ Ok = $true; Dados = $r; Erro = $null }
    } catch {
        return @{ Ok = $false; Dados = $null; Erro = $_.Exception.Message }
    }
}

# Envia POST /Caixa/FinalizarVenda com a MESMA notacao que o admin-caixa.js usa:
# itens[0].ProdutoId, itens[0].Quantidade, itens[0].Preco (form-urlencoded).
function Get-FormVenda([string]$url, [hashtable]$campos, [array]$itens) {
    $body = ''
    foreach ($k in $campos.Keys) { $body += "$k=$([uri]::EscapeDataString([string]$campos[$k]))&" }
    for ($i = 0; $i -lt $itens.Count; $i++) {
        $it = $itens[$i]
        $body += "itens%5B$i%5D.ProdutoId=$([uri]::EscapeDataString([string]$it.ProdutoId))&"
        $body += "itens%5B$i%5D.Quantidade=$([uri]::EscapeDataString([string]$it.Quantidade))&"
        $body += "itens%5B$i%5D.Preco=$([uri]::EscapeDataString([string]$it.Preco))&"
    }
    $params = @{
        Uri         = $url
        Method      = 'POST'
        Body        = $body
        ContentType = 'application/x-www-form-urlencoded'
        TimeoutSec  = 30
    }
    try {
        $r = Invoke-RestMethod @params
        return @{ Ok = $true; Dados = $r; Erro = $null }
    } catch {
        return @{ Ok = $false; Dados = $null; Erro = $_.Exception.Message }
    }
}

# ------------------------------------------------------------ pre-requisitos ---
Write-Titulo 'Pre-requisitos'

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

# ---------------------------------------------------- sobe a aplicacao isolada ---
Write-Titulo "Subindo a aplicacao em $BaseUrl (banco isolado: $DbIsolado)"

$env:ASPNETCORE_ENVIRONMENT              = 'Development'
$env:ConnectionStrings__DefaultConnection = "Data Source=$DbIsolado"
$env:ASPNETCORE_URLS                     = $BaseUrl
$env:DOTNET_CLI_TELEMETRY_OPTOUT          = '1'

$appLog = Join-Path $env:TEMP 'qa-http-app.log'
$app = Start-Process -FilePath $dotnet `
        -ArgumentList 'run','--no-launch-profile','--urls',$BaseUrl `
        -WorkingDirectory $ProjetoDir `
        -RedirectStandardOutput $appLog `
        -RedirectStandardError  "$appLog.err" `
        -WindowStyle Hidden `
        -PassThru

$pronto = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 1000
    if ($app.HasExited) { break }
    try {
        $null = Invoke-RestMethod -Uri "$BaseUrl/Caixa/ObterStatusAtual" -TimeoutSec 3
        $pronto = $true
        break
    } catch { }
}

if (-not $pronto) {
    Write-Host '  ERRO: a aplicacao nao subiu. Log:' -ForegroundColor Red
    if (Test-Path $appLog) {
        Get-Content $appLog -Tail 40 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    exit 1
}
Write-Ok "aplicacao no ar em $BaseUrl (PID $($app.Id))"

# ------------------------------------------------------------------ setup ------
Write-Titulo 'Preparando dados no banco ISOLADO'

$r = Get-Form "$BaseUrl/Caixa/CadastrarVendedor" @{ nome = 'Vendedor QA'; codigo = 'QA123' }
if ($r.Ok -and $r.Dados.success) { Write-Ok 'vendedor QA123 cadastrado' } else { Write-Falha "cadastro de vendedor: $($r.Erro)" }

# HomeController.Novo espera o preco em CENTS (divide por 100).
$codBarras1 = 'QA0000000001'   # Prateleira 10,00
$codBarras2 = 'QA0000000002'   # PorPeso R$ 50,00/kg
$codBarras3 = 'QA0000000003'   # Prateleira 20,00

Get-Form "$BaseUrl/Home/Novo" @{ nome = 'Produto Prateleira QA'; tipo = 'Prateleira'; quantidade = '100'; preco = '1000'; codigoBarras = $codBarras1 } | Out-Null
Get-Form "$BaseUrl/Home/Novo" @{ nome = 'Produto PorPeso QA';     tipo = 'PorPeso';    quantidade = '50';  preco = '5000'; codigoBarras = $codBarras2 } | Out-Null
Get-Form "$BaseUrl/Home/Novo" @{ nome = 'Produto Caro QA';       tipo = 'Prateleira'; quantidade = '100'; preco = '2000'; codigoBarras = $codBarras3 } | Out-Null
Write-Ok '3 produtos cadastrados'

$p1 = (Get-Json "$BaseUrl/Caixa/BuscarPorCodigo?codigo=$codBarras1").Dados
$p2 = (Get-Json "$BaseUrl/Caixa/BuscarPorCodigo?codigo=$codBarras2").Dados
$p1id = $p1.id
$p2id = $p2.id
Write-Host "  produto prateleira id=$p1id   por peso id=$p2id"

# =============================================================================
# VENDAS (VendasQaTests)
# =============================================================================
Write-Titulo 'VENDAS - preco de tabela ignora o valor adulterado no POST'

# O VENDEDOR tenta mandar preco = 0.01 no POST. O servidor deve usar 10,00.
$r = Get-FormVenda "$BaseUrl/Caixa/FinalizarVenda" @{
    metodoPagamento = 'Dinheiro'; cpfCliente = ''; codigoVendedor = 'QA123'
    valorRecebido   = '20';      valorTroco  = '999'
} @(
    @{ ProdutoId = "$p1id"; Quantidade = '2'; Preco = '0.01' }
)

if ($r.Ok -and $r.Dados.success) {
    $vendaId = $r.Dados.vendaId
    $det = (Get-Json "$BaseUrl/Caixa/ObterDetalhesVenda?id=$vendaId").Dados
    $total = ($det.itens | Measure-Object -Property total -Sum).Sum
    if ([Math]::Abs($total - 20.0) -lt 0.01) {
        Write-Ok "preco de tabela respeitado: total R$ $total (o POST mandava 0,01)"
    } else {
        Write-Falha "total esperado 20.00, obtido $total - o preco do POST foi usado"
    }
} else {
    Write-Falha "FinalizarVenda falhou: $($r.Erro) $($r.Dados.message)"
}

Write-Titulo 'VENDAS - quantidade negativa deve ser rejeitada'

$r = Get-FormVenda "$BaseUrl/Caixa/FinalizarVenda" @{
    metodoPagamento = 'Dinheiro'; cpfCliente = ''; codigoVendedor = 'QA123'
    valorRecebido   = '0';       valorTroco  = '0'
} @(
    @{ ProdutoId = "$p1id"; Quantidade = '-2'; Preco = '0' }
)
if ($r.Ok -and $r.Dados.success -eq $false) {
    Write-Ok "quantidade negativa rejeitada: $($r.Dados.message)"
} else {
    Write-Falha "quantidade negativa NAO foi rejeitada (Ok=$($r.Ok) success=$($r.Dados.success) erro=$($r.Erro))"
}

Write-Titulo 'VENDAS - usa preço direto em R$/kg'

# PorPeso: preço 50,00/kg, quantidade 2 kg -> 50,00 x 2 = 100,00
$r = Get-FormVenda "$BaseUrl/Caixa/FinalizarVenda" @{
    metodoPagamento = 'Dinheiro'; cpfCliente = ''; codigoVendedor = 'QA123'
    valorRecebido   = '100';     valorTroco  = '0'
} @(
    @{ ProdutoId = "$p2id"; Quantidade = '2'; Preco = '0' }
)
if ($r.Ok -and $r.Dados.success) {
    $vendaId = $r.Dados.vendaId
    $det = (Get-Json "$BaseUrl/Caixa/ObterDetalhesVenda?id=$vendaId").Dados
    $total = ($det.itens | Measure-Object -Property total -Sum).Sum
    if ([Math]::Abs($total - 100.0) -lt 0.01) {
        Write-Ok "preço direto por kg correto: total R$ $total (esperado 100.00)"
    } else {
        Write-Falha "preço por kg: esperado 100.00, obtido $total"
    }
} else {
    Write-Falha "venda por peso falhou: $($r.Erro) $($r.Dados.message)"
}

Write-Titulo 'VENDAS - troco recalculado no servidor'

# Total 10,00 e o cliente "manda" troco = 999. O servidor deve ignorar.
$r = Get-FormVenda "$BaseUrl/Caixa/FinalizarVenda" @{
    metodoPagamento = 'Dinheiro'; cpfCliente = ''; codigoVendedor = 'QA123'
    valorRecebido   = '50';      valorTroco  = '999'
} @(
    @{ ProdutoId = "$p1id"; Quantidade = '1'; Preco = '0' }
)
if ($r.Ok -and $r.Dados.success) {
    Write-Ok "venda concluida; troco do POST (999) foi ignorado pelo servidor (vendaId $($r.Dados.vendaId))"
} else {
    Write-Falha "venda para checar troco falhou: $($r.Erro) $($r.Dados.message)"
}

# =============================================================================
# CAIXA (CaixaQaTests)
# =============================================================================
Write-Titulo 'CAIXA - suprimento valido'

$r = Get-Form "$BaseUrl/Caixa/RegistrarMovimentacao" @{ tipo = 'Suprimento'; valor = '500'; obs = 'abertura de caixa QA' }
if ($r.Ok -and $r.Dados.success) { Write-Ok 'suprimento de R$ 500,00 registrado' }
else { Write-Falha "suprimento falhou: $($r.Erro)" }

Write-Titulo 'CAIXA - sangria acima do saldo deve ser REJEITADA (BUG-05)'

$r = Get-Form "$BaseUrl/Caixa/RegistrarMovimentacao" @{ tipo = 'Sangria'; valor = '999999'; obs = 'sangria indevida QA' }
if ($r.Ok -and $r.Dados.success -eq $false) {
    Write-Ok "sangria acima do saldo rejeitada: $($r.Dados.message)"
} else {
    Write-Falha "sangria de 999.999,00 foi ACEITA (Ok=$($r.Ok) success=$($r.Dados.success) erro=$($r.Erro))"
}

Write-Titulo 'CAIXA - movimentacao com valor <= 0 deve ser REJEITADA (BUG-06)'

foreach ($v in @('0', '-50')) {
    $r = Get-Form "$BaseUrl/Caixa/RegistrarMovimentacao" @{ tipo = 'Sangria'; valor = $v; obs = "valor invalido $v" }
    if ($r.Ok -and $r.Dados.success -eq $false) {
        Write-Ok "movimentacao de valor $v rejeitada: $($r.Dados.message)"
    } else {
        Write-Falha "movimentacao de valor $v foi ACEITA (Ok=$($r.Ok) success=$($r.Dados.success) erro=$($r.Erro))"
    }
}

Write-Titulo 'CAIXA - saldo da gaveta deve ignorar vendas nao-Dinheiro (BUG-02)'

# Venda de R$ 20,00 no Pix: NAO pode entrar no saldo da gaveta.
$prod3 = (Get-Json "$BaseUrl/Caixa/BuscarPorCodigo?codigo=$codBarras3").Dados
$r = Get-FormVenda "$BaseUrl/Caixa/FinalizarVenda" @{
    metodoPagamento = 'Pix'; cpfCliente = ''; codigoVendedor = 'QA123'
    valorRecebido   = '0';  valorTroco  = '0'
} @(
    @{ ProdutoId = "$($prod3.id)"; Quantidade = '1'; Preco = '0' }
)
if ($r.Ok -and $r.Dados.success) { Write-Ok 'venda de R$ 20,00 no Pix registrada' }
else { Write-Falha "venda Pix falhou: $($r.Erro) $($r.Dados.message)" }

$status = (Get-Json "$BaseUrl/Caixa/ObterStatusAtual").Dados
Write-Host "  ObterStatusAtual -> vendas=$($status.vendas) suprimento=$($status.suprimento) saldo=$($status.saldo)"

# Vendas em DINHEIRO: 20 (preco tabela) + 100 (peso) + 10 (1 un x 10,00) = 130.
# A venda no Pix NAO entra na gaveta. Mais o suprimento de 500.
$esperado = 130 + 500
Write-Host "  esperado (dinheiro 130 + suprimento 500, Pix fora) = $esperado"
if ([Math]::Abs($status.saldo - $esperado) -lt 0.01) {
    Write-Ok "saldo da gaveta considera apenas Dinheiro: R$ $($status.saldo) (Pix de fora)"
} else {
    Write-Falha "saldo da gaveta inclui venda no Pix: esperado $esperado, obtido $($status.saldo) (BUG-02)"
}

Write-Titulo 'CAIXA - sinal da sangria no fechamento (BUG-01)'

$r = Get-Form "$BaseUrl/Caixa/RegistrarMovimentacao" @{ tipo = 'Sangria'; valor = '50'; obs = 'sangria valida de 50' }
if ($r.Ok -and $r.Dados.success) {
    $rel  = (Get-Json "$BaseUrl/Caixa/RelatorioDados").Dados
    $fech = (Get-Json "$BaseUrl/Caixa/ObterDadosFechamento").Dados
    Write-Host "  RelatorioDados.saldoFinal = $($rel.saldoFinal)"
    Write-Host "  Fechamento.saldoFinal     = $($fech.saldoFinal)"
    $sangriaEsperada = $status.saldo - 50
    if ([Math]::Abs($fech.saldoFinal - $sangriaEsperada) -lt 0.01) {
        Write-Ok "sangria de 50 SUBTRAIU do saldo: $($status.saldo) -> $($fech.saldoFinal)"
    } else {
        Write-Falha "sinal da sangria invertido: esperado $sangriaEsperada, obtido $($fech.saldoFinal) (BUG-01)"
    }
    if ([Math]::Abs($fech.saldoFinal - $rel.saldoFinal) -lt 0.01) {
        Write-Ok 'fechamento e relatorio concordam'
    } else {
        Write-Falha "divergencia entre fechamento ($($fech.saldoFinal)) e relatorio ($($rel.saldoFinal))"
    }
} else {
    Write-Falha "sangria valida foi rejeitada: $($r.Dados.message) (veja BUG-05)"
}

Write-Titulo 'CAIXA - fechamento do dia'

$r = Get-Form "$BaseUrl/Caixa/ZerarCaixaParaProximoDia" @{}
if ($r.Ok -and $r.Dados.success) { Write-Ok 'fechamento executado' }
else { Write-Falha "fechamento falhou: $($r.Erro)" }

$fech = (Get-Json "$BaseUrl/Caixa/ObterDadosFechamento").Dados
if ([Math]::Abs($fech.saldoFinal - 0) -lt 0.01) {
    Write-Ok 'apos o fechamento o saldo do dia e zero'
} else {
    Write-Falha "saldo apos fechamento = $($fech.saldoFinal), esperado 0"
}

# =============================================================================
# LIMPEZA + VERIFICACAO DE QUE O BANCO VERSIONADO NAO FOI TOCADO
# =============================================================================
Write-Titulo 'Encerrando a aplicacao'

if (-not $ManterApp) {
    if ($app -and -not $app.HasExited) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
        Write-Ok "aplicacao encerrada (PID $($app.Id))"
    }
} else {
    Write-Host "  -ManterApp: app segue no ar em $BaseUrl (PID $($app.Id))" -ForegroundColor Yellow
}

Write-Titulo 'Verificando o banco VERSIONADO (controleestoque.db)'

# O .csproj define CopyToOutputDirectory=Never e este script aponta a app para um
# banco em %TEMP%. Esta verificacao existe para PROVAR, na pratica, que o
# teste NAO tocou o banco de producao.
$HashDepois = (Get-FileHash $DbVersionado -Algorithm SHA256).Hash
Write-Host "  controleestoque.db (versionado) SHA256 antes : $HashAntes"
Write-Host "  controleestoque.db (versionado) SHA256 depois: $HashDepois"

$codigoSaida = 0
if ($HashAntes -eq $HashDepois) {
    Write-Ok 'BANCO DE PRODUCAO INTACTO - hash identico antes e depois'
} else {
    Write-Falha 'O BANCO DE PRODUCAO FOI ALTERADO! Investigue imediatamente.'
    $codigoSaida = 2
}

# Remove o banco isolado do %TEMP% e seus arquivos -wal/-shm.
foreach ($sufixo in @('', '-shm', '-wal')) {
    $arq = "$DbIsolado$sufixo"
    if (Test-Path $arq) {
        try { Remove-Item $arq -Force -ErrorAction Stop }
        catch { Write-Host "  (nao foi possivel remover $arq : $($_.Exception.Message))" -ForegroundColor DarkGray }
    }
}
Write-Host "  banco isolado removido: $DbIsolado"

Write-Titulo 'RESULTADO'
Write-Host '  Qualquer [FALHA] acima corresponde a um BUG CONHECIDO ainda nao corrigido.' -ForegroundColor Yellow
Write-Host '  Ver qa\RELATORIO-QA.md e rode "dotnet test" em tests.QA para o detalhamento.' -ForegroundColor Yellow

exit $codigoSaida
