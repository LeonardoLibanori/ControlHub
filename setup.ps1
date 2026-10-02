<#
.SYNOPSIS
    Prepara uma maquina NOVA para rodar o sistema. Rodar UMA VEZ por maquina.

.DESCRIPTION
    Faz tudo que antes era feito na mao, em um comando so:

      1. clone (ou reaproveita, se o repositorio ja estiver na maquina)
      2. checkout da main
      3. copia o banco de exemplo para o banco de trabalho (-ComDados)
      4. dotnet tool restore   (manifesto local: dotnet-ef)
      5. dotnet restore        (pacotes NuGet)
      6. dotnet build
      7. lista as migrations e AVISA se houver pendentes
      8. roda a suite de testes (pula com -PularTestes)

    DEPOIS DO SETUP, O DIA A DIA E SO "git pull" e "git push". Rerodar este
    script numa maquina pronta apenas ressincroniza - ele nunca sobrescreve o
    controleestoque.db.

    POR QUE O PASSO 7 AVISA E NAO APLICA
    ------------------------------------
    O Program.cs chama context.Database.Migrate() no startup. Traduzindo:
    "dotnet run" APLICA AS MIGRATIONS PENDENTES SOZINHO, sem prompt e sem
    confirmacao. Se a migration altera dados que ja existem (e varias fazem
    isso - ver Migrations/20260930134556_DecimalParaCentavos.cs, que reescreve
    colunas), o dano e silencioso.

    Por isso este script NUNCA aplica migration sozinho. Ele mostra o estado e
    deixa a decisao na mao de quem esta na frente da tela. Aplicar e preciso
    pedir de proposito:  .\setup.ps1 -AplicarMigrations

.NOTES
    Requer ExecutionPolicy Bypass:
        powershell -ExecutionPolicy Bypass -File .\setup.ps1

    O banco de trabalho (controleestoque.db) E VERSIONADO no Git (decisao de
    02/10/2026) para que as duas maquinas tenham os mesmos logins e dados. Por
    ser binario, ele nao pode ser mergeado: use .\sincronizar.ps1 -Enviar /
    -Receber no dia a dia (ver SINCRONIZAR.md). Este script cuida so da
    preparacao inicial da maquina.

.EXAMPLE
    Maquina nova, so para desenvolver/testar:
        powershell -ExecutionPolicy Bypass -File .\setup.ps1 -ComDados

.EXAMPLE
    Maquina da loja, com os dados reais copiados do PC antigo:
        powershell -ExecutionPolicy Bypass -File .\setup.ps1

.EXAMPLE
    Aplicar migrations depois de ler o SQL delas:
        powershell -ExecutionPolicy Bypass -File .\setup.ps1 -AplicarMigrations

.EXAMPLE
    Sincronizar uma maquina ja pronta (o dia a dia):
        git pull origin main
#>
[CmdletBinding()]
param(
    # Onde o projeto vai ficar (ou onde ja esta).
    [string]$Destino = 'E:\Sistema de Estoque\ControleEstoqueLoja',

    # Endereco usado quando ainda nao existe pasta no Destino.
    [string]$Repositorio = 'https://github.com/LeonardoLibanori/ControleEstoqueLoja.git',

    # Copia controleestoque.exemplo.db -> controleestoque.db (dados ficticios).
    # NAO use na maquina da loja: la o banco real precisa ser copiado da loja.
    [switch]$ComDados,

    # Aplica as migrations pendentes. Fora do padrao de proposito.
    [switch]$AplicarMigrations,

    [switch]$PularBuild,
    [switch]$PularTestes
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ funcoes ---

function Passo {
    param([string]$Mensagem)
    Write-Host ''
    Write-Host "=== $Mensagem ===" -ForegroundColor Cyan
}

function Ok {
    param([string]$Mensagem)
    Write-Host "  [OK]    $Mensagem" -ForegroundColor Green
}

function Info {
    param([string]$Mensagem)
    Write-Host "  $Mensagem"
}

function Aviso {
    param([string]$Mensagem)
    Write-Host "  [!]    $Mensagem" -ForegroundColor Yellow
}

function Falha {
    param([string]$Mensagem)
    Write-Host "  [FALHA] $Mensagem" -ForegroundColor Red
}

# Roda um comando nativo devolvendo a saida como texto.
#
# POR QUE O $ErrorActionPreference BAIXO AQUI: no Windows PowerShell 5.1, um
# comando nativo que escreve no stderr (dotnet faz isso com os warnings do
# NuGet) vira um erro terminante quando a preferencia e 'Stop'. Como aqui so
# interessa o stdout, a preferencia e baixada dentro da chamada e devolvida
# depois.
function Invoke-Nativo {
    param(
        [Parameter(Mandatory = $true)][string]$Programa,
        [Parameter(Mandatory = $true)][string[]]$Argumentos,
        [Parameter(Mandatory = $true)][string]$Diretorio
    )

    $preferenciaAnterior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        Push-Location $Diretorio
        try {
            return (& $Programa @Argumentos 2>&1 | Out-String)
        }
        finally {
            Pop-Location
        }
    }
    finally {
        $ErrorActionPreference = $preferenciaAnterior
    }
}
# Le as migrations do EF e devolve as que faltam aplicar.
#
# A saida de "dotnet ef migrations list" traz uma linha por migration e marca
# as nao aplicadas assim (medido nesta maquina):
#
#   20260220171624_Inicial_Nova
#   20260930134556_DecimalParaCentavos (Pending)
#
# O "(Pending)" e o unico sinal confiavel, entao a busca e feita por ele e nao
# por posicao - assim a ordem do texto ou um warning do NuGet no meio da
# saida nao quebram a leitura.
#
# POR QUE Mode=ReadOnly NA CONEXAO
# --------------------------------
# Esta funcao so LE o __EFMigrationsHistory, mas mesmo assim a conexao normal
# do Microsoft.Data.Sqlite abre o arquivo em modo leitura+escrita. Medido aqui:
#
#   antes  hash 300A6001...  -wal 57712 bytes
#   depois hash 792D4A3F...  -wal 0 bytes (arquivo removido)
#
# Nenhum dado mudou - as 4 migrations, os produtos e o vendedor continuam
# iguais, e o .db continua com 139264 bytes. O que houve foi um CHECKPOINT DE
# WAL: o SQLite consolidou o -wal dentro do .db no fechamento e apagou os
# arquivos. O conteudo e legitimo, mas o arquivo mudou de bytes.
#
# Num projeto em que o banco da loja nao e versionado e todo script de QA
# compara hash antes/depois para provar que nao encostou no dado, isso nao pode
# passar. Com Mode=ReadOnly o hash fica identico e o -wal sobrevive.
function Get-MigrationsPendentes {
    param(
        [Parameter(Mandatory = $true)][string]$Diretorio,
        [Parameter(Mandatory = $true)][string]$Banco,
        [switch]$SomenteLeitura
    )

    $variavelAnterior = $env:ConnectionStrings__DefaultConnection
    $conexao = if ($SomenteLeitura) {
        "Data Source=$Banco;Mode=ReadOnly"
    }
    else {
        "Data Source=$Banco"
    }
    $env:ConnectionStrings__DefaultConnection = $conexao
    try {
        # --no-build: a build ja foi feita acima; evita recompilar e ainda
        # reduz o barulho dos warnings do NuGet na saida.
        $saida = Invoke-Nativo -Programa 'dotnet' `
            -Argumentos @('ef', 'migrations', 'list', '--no-build') `
            -Diretorio $Diretorio
    }
    finally {
        $env:ConnectionStrings__DefaultConnection = $variavelAnterior
    }

    $pendentes = New-Object System.Collections.Generic.List[string]
    foreach ($linha in ($saida -split "`r?`n")) {
        $m = [regex]::Match($linha, '^\s*(\d{14})_([A-Za-z0-9_]+)\s*\(Pending\)')
        if ($m.Success) {
            $pendentes.Add("$($m.Groups[1].Value)_$($m.Groups[2].Value)")
        }
    }

    return $pendentes
}

function Write-AvisoMigrationsPendentes {
    param([string[]]$Pendentes)

    $quantidade = if ($Pendentes.Count -eq 1) { '1 migration' } else { "$($Pendentes.Count) migrations" }

    # Bloco de alerta: Write-Host puro (nao passa por pipeline) e cor de fundo
    # para nao passar despercebido no meio do log do setup.
    Write-Host ''
    Write-Host '  ==================================================================' -ForegroundColor Black -BackgroundColor Yellow
    Write-Host '   ATENCAO: HAI MIGRATIONS PENDENTES NESTE BANCO' -ForegroundColor Black -BackgroundColor Yellow
    Write-Host '  ==================================================================' -ForegroundColor Black -BackgroundColor Yellow
    Write-Host ''
    Write-Host "  $quantidade ainda NAO aplicada(s) neste controleestoque.db:" -ForegroundColor Yellow
    Write-Host ''
    foreach ($p in $Pendentes) {
        Write-Host "      $p" -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host '  POR QUE ISSO PRECISA DA SUA ATENCAO' -ForegroundColor Yellow
    Write-Host '  ---------------------------------' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  O Program.cs chama Database.Migrate() no start da aplicacao. Portanto:'
    Write-Host ''
    Write-Host '      dotnet run   ->   APLICA TUDO ISSO SOZINHO, SEM PERGUNTAR' -ForegroundColor White
    Write-Host ''
    Write-Host '  Nao existe prompt nem confirmacao antes disso. Se a migration alterar'
    Write-Host '  dados que ja existem, o estrago e silencioso e so aparece depois.'
    Write-Host ''
    Write-Host '  COMO APLICAR COM CONSCIENCIA' -ForegroundColor Yellow
    Write-Host '  -----------------------------' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  1) Backup antes de tudo:'
    Write-Host '       Copy-Item controleestoque.db "$env:TEMP\backup-$(Get-Date -Format ''yyyyMMdd-HHmmss'').db"'
    Write-Host ''
    Write-Host '  2) Leia o SQL da migration em Migrations/ e entenda o que ela faz nos dados.'
    Write-Host ''
    Write-Host '  3) Aplique de proposito:'
    Write-Host '       .\setup.ps1 -AplicarMigrations'
    Write-Host ''
    Write-Host '  4) Para conferir antes, sem tocar no banco:'
    Write-Host '       Copy-Item controleestoque.db "$env:TEMP\teste.db"'
    Write-Host '       dotnet ef database update --connection "Data Source=$env:TEMP\teste.db"'
    Write-Host ''
    Write-Host '  OBS: nem toda migration tem Down() exato. A DecimalParaCentavos tem Down()'
    Write-Host '  escrito, mas a conversao de volta nao e garantida em todos os casos. Por'
    Write-Host '  isso o backup do passo 1 nao e opcional.'
    Write-Host ''
}
# =============================================================================
# 1) REPOSITORIO
# =============================================================================
Passo 'Repositorio'

if (Test-Path (Join-Path $Destino '.git')) {
    Info "repositorio encontrado em $Destino - sincronizando em vez de clonar"
    Invoke-Nativo -Programa 'git' -Argumentos @('-C', $Destino, 'fetch', 'origin') -Diretorio $Destino | Out-Null
    Invoke-Nativo -Programa 'git' -Argumentos @('-C', $Destino, 'checkout', 'main') -Diretorio $Destino | Out-Null
    $saida = Invoke-Nativo -Programa 'git' -Argumentos @('-C', $Destino, 'pull', 'origin', 'main') -Diretorio $Destino
    if ($saida -match 'Already up to date') { Ok 'ja estava em dia com o remoto' }
    else { Ok 'atualizado com o remoto' }
}
else {
    Info "clonando $Repositorio"
    Info "para $Destino"
    $saida = Invoke-Nativo -Programa 'git' -Argumentos @('clone', $Repositorio, $Destino) -Diretorio (Split-Path -Parent $Destino)
    if ($LASTEXITCODE -ne 0) {
        Falha 'clone falhou'
        Write-Host $saida -ForegroundColor Red
        exit 1
    }
    Ok 'clone concluido'
    Invoke-Nativo -Programa 'git' -Argumentos @('-C', $Destino, 'checkout', 'main') -Diretorio $Destino | Out-Null
}

if (-not (Test-Path (Join-Path $Destino 'ControleEstoqueLoja.csproj'))) {
    Falha "nao achei ControleEstoqueLoja.csproj em $Destino - o destino parece errado"
    exit 1
}
Ok "projeto em $Destino"

# =============================================================================
# 2) BANCO DE TRABALHO
# =============================================================================
Passo 'Banco de trabalho (controleestoque.db)'

$exemplo = Join-Path $Destino 'controleestoque.exemplo.db'
$real    = Join-Path $Destino 'controleestoque.db'

if (Test-Path $real) {
    Ok 'banco ja existe - NAO foi tocado (ele guarda os dados da maquina)'
}
elseif ($ComDados) {
    if (-not (Test-Path $exemplo)) {
        Falha 'nao achei controleestoque.exemplo.db no repositorio'
        exit 1
    }
    Copy-Item $exemplo $real
    Ok 'copiado do banco de exemplo (dados ficticios, bons para testar)'
    Info 'para a loja real, copie o controleestoque.db do PC antigo em vez disto'
}
else {
    Falha 'nao existe controleestoque.db e voce nao passou -ComDados'
    Info '  - para testar com dados ficticios:  .\setup.ps1 -ComDados'
    Info '  - para a loja: copie o controleestoque.db do PC antigo para:'
    Info "      $real"
    Info '  - do zero (so o schema, sem dados): delete o arquivo e suba a aplicacao'
    exit 1
}

# =============================================================================
# 3) FERRAMENTAS E PACOTES
# =============================================================================
Passo 'Manifesto de ferramentas (dotnet-ef)'
$saida = Invoke-Nativo -Programa 'dotnet' -Argumentos @('tool', 'restore') -Diretorio $Destino
if ($LASTEXITCODE -ne 0) { Falha 'dotnet tool restore falhou'; Write-Host $saida -ForegroundColor Red; exit 1 }
Ok 'dotnet-ef disponivel (versao fixada em dotnet-tools.json)'

Passo 'Restaurando pacotes NuGet'
$saida = Invoke-Nativo -Programa 'dotnet' -Argumentos @('restore') -Diretorio $Destino
if ($LASTEXITCODE -ne 0) { Falha 'dotnet restore falhou'; Write-Host $saida -ForegroundColor Red; exit 1 }
Ok 'pacotes restaurados'

# =============================================================================
# 4) BUILD
# =============================================================================
if ($PularBuild) {
    Passo 'Build (pulado por parametro)'
}
else {
    Passo 'Build'
    $saida = Invoke-Nativo -Programa 'dotnet' -Argumentos @('build', '--nologo', '-v', 'quiet') -Diretorio $Destino
    if ($LASTEXITCODE -ne 0) { Falha 'build falhou'; Write-Host $saida -ForegroundColor Red; exit 1 }
    if ($saida -match '(\d+)\s+Erro\(s\)') {
        if ([int]$Matches[1] -gt 0) { Falha "$($Matches[1]) erro(s) no build"; Write-Host $saida -ForegroundColor Red; exit 1 }
    }
    Ok 'build ok'
}
# =============================================================================
# 5) MIGRATIONS - aqui o script so AVISA. Nunca aplica sozinho.
# =============================================================================
Passo 'Estado das migrations'

# O hash e conferido antes e depois porque este projeto trata o banco da loja
# como intocavel: todo script de QA faz a mesma verificacao. A leitura das
# migrations abre o arquivo, e um checkpoint de WAL silencioso mudaria os
# bytes sem mudar dado nenhum - ver o comentario de Get-MigrationsPendentes.
$hashAntes = (Get-FileHash $real -Algorithm SHA256).Hash

$pendentes = @()
try {
    $pendentes = @(Get-MigrationsPendentes -Diretorio $Destino -Banco $real -SomenteLeitura)
    Ok "$($pendentes.Count) migration(s) pendente(s) neste banco"
}
catch {
    Aviso "nao consegui ler as migrations: $($_.Exception.Message)"
    Info '  confira manualmente com:  dotnet ef migrations list'
}

$hashDepois = (Get-FileHash $real -Algorithm SHA256).Hash
if ($hashAntes -eq $hashDepois) {
    Ok 'banco nao foi modificado pela leitura (hash igual)'
}
else {
    Falha 'o banco mudou de bytes durante a leitura das migrations'
    Info "  antes: $hashAntes"
    Info "  depois: $hashDepois"
    Info '  o script nao aplicou nada, mas confira o arquivo antes de seguir'
}

if ($pendentes.Count -gt 0) {
    Write-AvisoMigrationsPendentes -Pendentes $pendentes
}

if ($AplicarMigrations) {
    if ($pendentes.Count -eq 0) {
        Info 'nada pendente - nada a fazer'
    }
    else {
        Passo 'Aplicando migrations (pedido com -AplicarMigrations)'
        $saida = Invoke-Nativo -Programa 'dotnet' -Argumentos @('ef', 'database', 'update', '--no-build') -Diretorio $Destino
        if ($LASTEXITCODE -ne 0) {
            Falha 'dotnet ef database update falhou'
            Write-Host $saida -ForegroundColor Red
            Info 'o banco pode ter ficado no meio do caminho - restaure o backup'
            exit 1
        }
        Ok 'migrations aplicadas'
        $restam = @(Get-MigrationsPendentes -Diretorio $Destino -Banco $real)
        if ($restam.Count -eq 0) { Ok 'banco em dia com o codigo' }
        else { Aviso "ainda restam $($restam.Count) migration(s) pendente(s)" }
    }
}
elseif ($pendentes.Count -gt 0) {
    Write-Host ''
    Info 'migrations NAO aplicadas (comportamento padrao deste script).'
    Info 'Se voce subiu a aplicacao depois disso, elas foram aplicadas na hora.'
}

# =============================================================================
# 6) TESTES
# =============================================================================
if ($PularTestes) {
    Passo 'Testes (pulados por parametro)'
}
else {
    Passo 'Suite de testes (tests.QA)'
    $projTestes = Join-Path $Destino 'tests.QA\tests.QA.csproj'
    if (-not (Test-Path $projTestes)) {
        Aviso 'tests.QA nao encontrado - pulando'
    }
    else {
        # A suite cria o proprio banco em %TEMP% e confere o hash do
        # controleestoque.db antes e depois. O banco real nao e tocado.
        $saida = Invoke-Nativo -Programa 'dotnet' `
            -Argumentos @('test', '"' + $projTestes + '"', '--nologo') `
            -Diretorio $Destino
        $resumo = ($saida -split "`r?`n" | Where-Object { $_ -match 'Aprovado|falha' } | Select-Object -Last 1)
        if ($resumo) { Info $resumo.Trim() }
        if ($saida -match 'Com falha:\s+(\d+)') {
            if ([int]$Matches[1] -gt 0) {
                Aviso "$($Matches[1]) teste(s) falhando - veja qa/RELATORIO-QA.md"
            }
            else {
                Ok 'suite verde'
            }
        }
    }
}

# =============================================================================
Write-Host ''
Write-Host '==================================================================' -ForegroundColor Green
Write-Host '  Pronto. Para rodar a aplicacao:' -ForegroundColor Green
Write-Host ''
Write-Host '      dotnet run' -ForegroundColor White
Write-Host ''
Write-Host '  http://localhost:5236' -ForegroundColor White
Write-Host '==================================================================' -ForegroundColor Green
Write-Host ''
Write-Host 'Depois do setup, o dia a dia e so:' -ForegroundColor Gray
Write-Host '      git pull origin main      (buscar)' -ForegroundColor Gray
Write-Host '      git push origin main      (enviar)' -ForegroundColor Gray
Write-Host ''