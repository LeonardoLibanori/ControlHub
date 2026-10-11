<#
.SYNOPSIS
    Script para subir/parar a instância de teste na porta 5311 com banco temporário.
.DESCRIPTION
    Cria um .db temporário, configura a connection string e sobe a aplicação.
    Usado pelos guards de QA (shell-check, nav-smoke, layout-check, etc).
.PARAMETER Acao
    "subir" para iniciar a instância, "parar" para derrubar.
.EXAMPLE
    qa/app-teste.ps1 -Acao subir
    qa/app-teste.ps1 -Acao parar
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('subir','parar')]
    [string]$Acao
)

$ErrorActionPreference = 'Stop'

$repoRoot = "E:\Sistema de Estoque"
$trxDir = Join-Path $repoRoot "trx"
$dbPath = Join-Path $trxDir "app-teste.db"
$pidFile = Join-Path $trxDir "app-teste.pid"
$baseUrl = "http://127.0.0.1:5311"

if (-not (Test-Path $trxDir)) {
    New-Item -ItemType Directory -Path $trxDir | Out-Null
}

function Subir {
    Write-Host "=== SUBINDO INSTÂNCIA DE TESTE ===" -ForegroundColor Green

    # Apaga .db e arquivos auxiliares anteriores
    foreach ($ext in @("", "-wal", "-shm")) {
        $f = $dbPath + $ext
        if (Test-Path $f) {
            Remove-Item -Force $f
            Write-Host "Removido: $f"
        }
    }

    # Remove PID anterior
    if (Test-Path $pidFile) {
        Remove-Item -Force $pidFile
    }

    # Connection string para o .db temporário
    $connStr = "Data Source=$dbPath;Cache=Shared"
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:ASPNETCORE_URLS = "http://127.0.0.1:5311"
    $env:ASPNETCORE_ENVIRONMENT = "Development"

    Write-Host "ConnectionString: $connStr"
    Write-Host "URLs: $($env:ASPNETCORE_URLS)"

    # Inicia o processo
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "run --no-build --no-launch-profile --urls http://127.0.0.1:5311" `
        -WorkingDirectory (Join-Path $repoRoot "ControleEstoqueLoja") `
        -WindowStyle Hidden `
        -PassThru

    $p = $process.Id
    Write-Host "PID: $p"
    $p | Out-File -FilePath $pidFile -Encoding utf8

    # Aguarda a aplicação responder (até 60s)
    $timeout = 60
    $interval = 2
    $elapsed = 0
    $ready = $false

    Write-Host "Aguardando /Account/Login responder 200 (timeout ${timeout}s)..."
    while ($elapsed -lt $timeout) {
        try {
            $resp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method GET -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                $ready = $true
                break
            }
        } catch {
            # Ignora erros de conexão enquanto sobe
        }
        Start-Sleep -Seconds $interval
        $elapsed += $interval
    }

    if (-not $ready) {
        Write-Host "TIMEOUT: A aplicação não respondeu em ${timeout}s" -ForegroundColor Red
        Parar
        exit 1
    }

    Write-Host "PRONTO - PID: $p" -ForegroundColor Green
}

function Parar {
    Write-Host "=== PARANDO INSTÂNCIA DE TESTE ===" -ForegroundColor Yellow

    # Primeiro, mata qualquer processo na porta 5311
    $conn = Get-NetTCPConnection -LocalPort 5311 -State Listen -ErrorAction SilentlyContinue
    if ($conn) {
        $p = $conn.OwningProcess
        Write-Host "Matando processo na porta 5311 (PID $p)..."
        Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
    }

    # Também mata pelo PID file se existir
    if (Test-Path $pidFile) {
        $p = Get-Content $pidFile -Raw
        if ($p -match '^\d+$') {
            Write-Host "Matando processo PID $p e filhos..."
            $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
            if ($proc) {
                $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$p" -ErrorAction SilentlyContinue
                foreach ($c in $children) {
                    Stop-Process -Id $c.ProcessId -Force -ErrorAction SilentlyContinue
                }
                Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
                Write-Host "Processo $p finalizado."
            } else {
                Write-Host "Processo $p já não existe."
            }
        }
        Remove-Item -Force $pidFile -ErrorAction SilentlyContinue
    }

    # Aguarda um pouco para os handles de arquivo serem liberados
    Start-Sleep -Seconds 2

    # Remove .db e arquivos auxiliares
    foreach ($ext in @("", "-wal", "-shm")) {
        $f = $dbPath + $ext
        if (Test-Path $f) {
            try {
                Remove-Item -Force $f
                Write-Host "Removido: $f"
            } catch {
                Write-Host "Aviso: não foi possível remover $f : $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }

    # Verifica se a porta 5311 está livre
    $portInUse = Get-NetTCPConnection -LocalPort 5311 -State Listen -ErrorAction SilentlyContinue
    if ($portInUse) {
        Write-Host "ATENÇÃO: Porta 5311 ainda em uso!" -ForegroundColor Red
    } else {
        Write-Host "Porta 5311 livre." -ForegroundColor Green
    }
}

if ($Acao -eq 'subir') {
    Subir
} elseif ($Acao -eq 'parar') {
    Parar
}