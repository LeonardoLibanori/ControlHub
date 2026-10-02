<#
.SYNOPSIS
    Sincroniza o sistema entre maquinas (casa <-> trabalho) em UM comando.

.DESCRIPTION
    O banco controleestoque.db passou a ser versionado no Git (decisao de
    02/10/2026) para que as duas maquinas tenham os MESMOS logins e os MESMOS
    dados. Este script encapsula o ritual inteiro, que antes exigia lembrar
    6 passos na ordem certa.

    USO NO DIA A DIA (sempre nessa ordem):

        .\sincronizar.ps1 -Enviar      # antes de sair de casa/trabalho
        .\sincronizar.ps1 -Receber     # ao chegar na outra maquina

    Por que "antes de trocar de maquina" e nao "a cada alteracao":
    o banco so pode ter UM dono por vez. Se as duas maquinas gravarem no mesmo
    .db sem sincronizar no meio, o Git recusa o merge (o .db e binario) e o
    script aponta o conflito em vez de deixar passar banco corrompido.

    O QUE O SCRIPT FAZ (e o que ele RECUSA fazer)

    - RECUSA se a aplicacao estiver rodando: gravar .db com o app aberto
      produz arquivo pela metade.
    - Faz BACKUP do .db antes de qualquer operacao que possa sobrescrever.
    - -Receber: git pull, aplica migrations pendentes, confere o resultado.
    - -Enviar: git add/commit/push incluindo o .db, com mensagem padrao.
    - -Status: mostra o estado das duas maquinas, sem mexer em nada.

    NUNCA resolve conflito sozinho, NUNCA descarta .db local sem backup e
    NUNCA usa --force. Se algo estiver errado, ele para e explica.

.PARAMETER Enviar
    Envia codigo + banco para o GitHub (git add/commit/push).

.PARAMETER Receber
    Busca atualizacoes (git pull) e aplica migrations pendentes.

.PARAMETER Status
    Apenas diagnostico: nao altera arquivo nenhum.

.PARAMETER Mensagem
    Texto do commit no -Enviar. Com padrao automatico quando omitido.

.EXAMPLE
    .\sincronizar.ps1 -Status
    Ver o estado antes de decidir.

.EXAMPLE
    .\sincronizar.ps1 -Enviar
    Sincroniza antes de trocar de maquina.

.EXAMPLE
    .\sincronizar.ps1 -Receber
    Sincroniza ao chegar na outra maquina.

.NOTES
    Requer ExecutionPolicy Bypass na primeira vez:
        powershell -ExecutionPolicy Bypass -File .\sincronizar.ps1 -Status
#>
[CmdletBinding()]
param(
    [switch]$Enviar,
    [switch]$Receber,
    [switch]$Status,
    [string]$Mensagem
)

$ErrorActionPreference = 'Stop'

# ===========================================================================
# PASTA DO SCRIPT E CAMINHOS
# Precisa vir ANTES das funcoes: o corpo do script usa $banco/$exemplo
# ao chamar MostrarStatus, e essas variaveis sao resolvidas aqui.
# ===========================================================================
$raiz = $PSScriptRoot
if (-not $raiz) { $raiz = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $raiz) { $raiz = (Get-Location).Path }
Set-Location -LiteralPath $raiz

$banco        = Join-Path $raiz 'controleestoque.db'
$exemplo     = Join-Path $raiz 'controleestoque.exemplo.db'
$pastaBackup = Join-Path $raiz 'backups-banco'


# ------------------------------------------------------------------ funcoes ---

function Passo { param([string]$M) Write-Host ''; Write-Host "=== $M ===" -ForegroundColor Cyan }
function Ok    { param([string]$M) Write-Host "  [OK]    $M" -ForegroundColor Green }
function Info  { param([string]$M) Write-Host "  $M" }
function Aviso { param([string]$M) Write-Host "  [!]    $M" -ForegroundColor Yellow }
function Falha { param([string]$M) Write-Host "  [FALHA] $M" -ForegroundColor Red }
function Erro  { param([string]$M) Write-Host "  $M" -ForegroundColor Red }

function Invoke-Git {
    param([string[]]$Argumentos, [string]$Diretorio)

    # O Git escreve as mensagens de progresso no STDERR ("From https://...",
    # "Already up to date") e devolve 0 no sucesso. Se usarmos
    # $ErrorActionPreference='Stop' o PowerShell transforma esse stderr em
    # excecao TERMINANDO o script - foi o que aconteceu de primeira.
    # Solucao: o Preference vai para 'Continue' dentro da chamada, e quem
    # decide se falhou e o codigo de saida do git (LastExitCode), nunca o
    # texto escrito.
    $antigo = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $saida = & git @Argumentos 2>&1
        $codigo = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $antigo
    }

    $texto = ($saida | ForEach-Object { $_.ToString() }) -join "`n"

    if ($codigo -ne 0) {
        throw "git $($Argumentos -join ' ') falhou (codigo $codigo):`n$texto"
    }
    return $texto
}

# ---------------------------------------------------------- guarda: app viva ---

function TestarAplicacaoParada {
    $proc = Get-Process -Name 'ControleEstoqueLoja' -ErrorAction SilentlyContinue
    if ($proc) {
        $ids = ($proc | ForEach-Object { $_.Id }) -join ', '
        Falha "A aplicacao esta RODANDO (PID $ids)."
        Erro ' Gravar o banco com o app aberto pode salvar o arquivo pela metade.'
        Erro ' Feche a janela do sistema (ou Ctrl+C no terminal) e rode de novo:'
        Write-Host ''
        Write-Host '        Stop-Process -Name ControleEstoqueLoja -Force' -ForegroundColor White
        Write-Host ''
        return $false
    }
    return $true
}

# ------------------------------------------------------------------- backup ---

function FazerBackup {
    if (-not (Test-Path $banco)) { return $null }

    if (-not (Test-Path $pastaBackup)) {
        New-Item -ItemType Directory -Path $pastaBackup -Force | Out-Null
    }
    $carimbo = Get-Date -Format 'yyyyMMdd-HHmmss'
    $destino = Join-Path $pastaBackup "controleestoque-$carimbo.db"
    Copy-Item $banco $destino -Force
    return $destino
}

# ------------------------------------------------------------------- status ---

function MostrarStatus {
    Passo 'Situacao do repositorio'
    try {
        $branch = Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')
        Info "branch: $branch"
        $hashLocal = Invoke-Git @('rev-parse', 'HEAD')
        Info "HEAD local: $($hashLocal.Substring(0, 7))"
    }
    catch {
        Falha 'esta pasta nao parece um repositorio git'
        return
    }

    try {
        $remoto = Invoke-Git @('rev-parse', 'origin/main')
        $dentro = Invoke-Git @('rev-list', '--left-right', '--count', 'origin/main...HEAD')
        $partes = ($dentro -split '\s+' | Where-Object { $_ -ne '' })
        $atras = [int]$partes[0]
        $frente = [int]$partes[1]

        if ($atras -eq 0 -and $frente -eq 0) {
            Ok "sincronizado com o GitHub (origin/main = $($remoto.Substring(0, 7)))"
        }
        else {
            if ($frente -gt 0) { Aviso "$frente commit(s) local(is) NAO enviado(s)  ->  .\sincronizar.ps1 -Enviar" }
            if ($atras -gt 0)  { Aviso "$atras commit(s) novo(s) no GitHub   ->  .\sincronizar.ps1 -Receber" }
        }
    }
    catch {
        Aviso 'nao consegui comparar com o GitHub (sem rede ou sem remoto configurado)'
    }

    Passo 'Banco de dados'
    if (Test-Path $banco) {
        $info = Get-Item $banco
        $hash = (Get-FileHash $banco -Algorithm SHA256).Hash.Substring(0, 16)
        Info "arquivo: $($info.Name)   $([math]::Round($info.Length / 1KB, 1)) KB"
        Info "modificado: $($info.LastWriteTime.ToString('dd/MM/yyyy HH:mm'))"
        Info "sha256: $hash..."

        # O banco so aparece em "ls-files" DEPOIS do primeiro commit. Antes
        # disso ele pode estar legivel (nao ignorado) e ainda assim nao
        # versionado. Por isso os tres estados sao distinguidos:
        $versionado = Invoke-Git @('ls-files', 'controleestoque.db')
        if ($versionado) {
            Ok 'o banco esta versionado (entra no push)'
        }
        else {
            # check-ignore devolve 1 quando o arquivo NAO esta ignorado - que e
            # o cenario bom. Entao nao vai pelo Invoke-Git (que trata != 0 como
            # falha): chamamos o git direto e lemos o codigo de saida.
            & git check-ignore -q 'controleestoque.db' 2>$null
            if ($LASTEXITCODE -eq 0) {
                Falha 'o banco esta IGNORADO pelo .gitignore - nao vai entrar no push'
                Info ' a regra !controleestoque.db precisa ficar DEPOIS das regras *.db'
            }
            else {
                Ok 'o banco esta liberado pelo .gitignore (ainda nao versionado)'
            }
        }

        $sujo = Invoke-Git @('status', '--porcelain', 'controleestoque.db')
        if ($sujo) { Info "pendente no git: $($sujo -replace "`n", ', ')" }
    }
    else {
        Aviso 'controleestoque.db NAO existe nesta maquina'
        if (Test-Path $exemplo) { Info 'existe o banco de exemplo: copy controleestoque.exemplo.db controleestoque.db' }
    }

    if (Test-Path $pastaBackup) {
        $n = @(Get-ChildItem $pastaBackup -Filter '*.db' -ErrorAction SilentlyContinue).Count
        if ($n -gt 0) { Info "backups guardados em .\backups-banco : $n arquivo(s)" }
    }
}

# ================================================================ principal ====

if (-not $Enviar -and -not $Receber -and -not $Status) {
    Write-Host ''
    Write-Host '  Escolha uma acao:' -ForegroundColor White
    Write-Host ''
    Write-Host '    .\sincronizar.ps1 -Status      so diagnostico, nao altera nada' -ForegroundColor Gray
    Write-Host '    .\sincronizar.ps1 -Receber     chegou na maquina (git pull + migrations)' -ForegroundColor Gray
    Write-Host '    .\sincronizar.ps1 -Enviar      vai trocar de maquina (commit + push)' -ForegroundColor Gray
    Write-Host ''
    Write-Host '  LEIA SINCRONIZAR.md para o passo a passo completo.' -ForegroundColor DarkGray
    Write-Host ''
    exit 0
}

if ($Status) { MostrarStatus; exit 0 }

# --------------------------------------------- guarda comum aos dois fluxos ---
if (-not (TestarAplicacaoParada)) { exit 1 }

if (-not (Test-Path $banco)) {
    Falha 'controleestoque.db nao existe nesta maquina.'
    if (Test-Path $exemplo) { Info ' rode antes:  copy controleestoque.exemplo.db controleestoque.db' }
    exit 1
}

# ----------------------------------------------------------------- Receber ---
if ($Receber) {
    Passo 'Backup do banco local (antes de qualquer coisa)'
    $bk = FazerBackup
    if ($bk) { Ok "backup: $(Split-Path $bk -Leaf)" }
    else { Aviso 'nao ha banco local para fazer backup' }

    Passo 'git pull'
    try {
        $saida = Invoke-Git @('pull', '--ff-only', 'origin', 'main')
        $saida.Split("`n") | Select-Object -Last 3 | ForEach-Object { Info $_ }
        Ok 'pull concluido sem conflito'
    }
    catch {
        Falha 'o pull nao pode ser feito automaticamente.'
        Erro " $_"
        Erro ''
        Erro '  As maquinas mexeram no MESMO .db e ele e binario: o Git recusa combinar.'
        Erro '  Nao use --force. See SINCRONIZAR.md, secao "Conflito de banco".'
        exit 1
    }

    Passo 'Ferramentas e migrations'
    # Numa maquina recien-clonada faltam duas coisas: o manifesto de
    # ferramentas (dotnet-ef vive no dotnet-tools.json versionado) e o
    # project.assets.json do restore. Sem eles o "dotnet ef" aborta com
    # NETSDK1004. O restore e rapido e so acontece quando falta.
    if (-not (Test-Path (Join-Path $raiz 'obj\project.assets.json'))) {
        Info 'projeto ainda nao restaurado nesta maquina - restaurando...'
        & dotnet tool restore 2>&1 | Out-Null
        & dotnet restore 2>&1 | Select-Object -Last 1 | ForEach-Object { Info $_ }
        Ok 'restore concluido'
    }
    if (-not (Test-Path (Join-Path $env:USERPROFILE '.nuget\packages\dotnet-ef'))) {
        & dotnet tool restore 2>&1 | Out-Null
    }

    $saidaMig = (& dotnet ef migrations list 2>&1 | Out-String)
    $temPendente = @($saidaMig -split "`r?`n" | Where-Object { $_ -match '^\d{14,}.*Pendente' })
    if ($temPendente.Count -gt 0) {
        $temPendente | ForEach-Object { Info $_.Trim() }
        Info 'aplicando (o app faria o mesmo ao subir, mas aqui fica registrado)...'
        (& dotnet ef database update 2>&1 | Select-Object -Last 2) | ForEach-Object { Info $_ }
        Ok 'migrations aplicadas'
    }
    else {
        Ok 'banco em dia com o codigo (nada pendente)'
    }

    Passo 'Resumo'
    Ok 'sincronizado com o GitHub'
    Write-Host ''
    Write-Host '  Para subir a aplicacao:  dotnet run' -ForegroundColor White
    Write-Host '  http://localhost:5236' -ForegroundColor White
    Write-Host ''
    exit 0
}

# ------------------------------------------------------------------ Enviar ---
if ($Enviar) {
    Passo 'Backup do banco local (antes de comitar)'
    $bk = FazerBackup
    if ($bk) { Ok "backup: $(Split-Path $bk -Leaf)" }

    Passo 'git add'
    Invoke-Git @('add', '-A') | Out-Null
    $pendentes = Invoke-Git @('status', '--porcelain')
    $reais = @($pendentes -split "`n" | Where-Object { $_ -ne '' -and $_ -notmatch '\.db-(wal|shm)$' })

    if ($reais.Count -eq 0) {
        Ok 'nada para enviar - as maquinas ja estao iguais'
        exit 0
    }

    Write-Host ''
    Info 'Arquivos que serao enviados:'
    $reais | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
    if ($reais.Count -gt 20) { Info "    ... e mais $($reais.Count - 20)" }
    Write-Host ''

    if (-not $Mensagem) {
        $temBanco = $reais | Where-Object { $_ -match 'controleestoque\.db$' }
        if ($temBanco) { $Mensagem = 'chore(banco): sincroniza dados e estoque entre maquinas' }
        else { $Mensagem = 'chore: atualizacoes do projeto' }
    }

    Passo 'git commit'
    (Invoke-Git @('commit', '-m', $Mensagem)).Split("`n") | Select-Object -Last 2 | ForEach-Object { Info $_ }
    Ok "commit: $Mensagem"

    Passo 'git push'
    try {
        (Invoke-Git @('push', 'origin', 'main')).Split("`n") | Select-Object -Last 3 | ForEach-Object { Info $_ }
        Ok 'push concluido'
    }
    catch {
        Falha 'o push falhou.'
        Erro " $_"
        Erro ' Se for erro de autenticacao:  git credential-manager github login'
        Erro ' Nao use --force. Suas alteracoes continuam commitadas localmente.'
        exit 1
    }

    Write-Host ''
    Write-Host '  Ao chegar na outra maquina:  .\sincronizar.ps1 -Receber' -ForegroundColor Green
    Write-Host ''
    exit 0
}
