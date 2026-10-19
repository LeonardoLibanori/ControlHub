<#
.SYNOPSIS
    Senhas de teste GERADAS por execucao (Fase 0/C).
.DESCRIPTION
    NUNCA grava senha fixa aqui: usa SHELL_DONO_SENHA / SHELL_OPERADOR_SENHA
    (nomes da Fase 0/C) e o legado SHELL_OPER_SENHA quando existirem; senao
    GERA uma senha aleatoria que cumpre a politica do Identity em Program.cs
    (RequiredLength 6, maiuscula, minuscula, digito - mais simbolo por folga)
    UMA VEZ por execucao e a publica no $env: do processo para os scripts
    filhos usarem a mesma. Nenhuma senha em log ou arquivo.
.EXAMPLE
    . "$PSScriptRoot/senha-teste.ps1"
    $dono = Get-SenhaDono
#>

function New-SenhaAleatoria {
    $maius = 'ABCDEFGHJKLMNPQRSTUVWXYZ'.ToCharArray()
    $minus = 'abcdefghijkmnpqrstuvwxyz'.ToCharArray()
    $dig   = '23456789'.ToCharArray()
    $simb  = '!@#$%&*-+=?'.ToCharArray()
    $todas = $maius + $minus + $dig + $simb
    $r = New-Object System.Random
    $resto = -join (1..10 | ForEach-Object { $todas[$r.Next($todas.Length)] })
    return '' + $maius[$r.Next($maius.Length)] + $minus[$r.Next($minus.Length)] + $dig[$r.Next($dig.Length)] + $simb[$r.Next($simb.Length)] + $resto
}

function Resolve-Senha {
    param([string[]]$Nomes)
    foreach ($n in $Nomes) {
        $v = [Environment]::GetEnvironmentVariable($n, 'Process')
        if ($v) { return $v }
    }
    $s = New-SenhaAleatoria
    foreach ($n in $Nomes) { [Environment]::SetEnvironmentVariable($n, $s, 'Process') }
    return $s
}

function Get-SenhaDono { Resolve-Senha @('SHELL_DONO_SENHA') }

function Get-SenhaOperador { Resolve-Senha @('SHELL_OPERADOR_SENHA', 'SHELL_OPER_SENHA') }
