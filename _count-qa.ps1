$content = Get-Content $args[0] -Encoding UTF8
$f = $content | Where-Object { $_ -match '\[FALHA\]' }
$cat = @{}
foreach ($line in $f) {
    $mm = [regex]::Match($line, '^\s*dono/([^/]+/[^/]+)/[0-9]+px\s+(.*)$')
    if ($mm.Success) {
        $k = "$($mm.Groups[1].Value)|$($mm.Groups[2].Value)"
        if (-not $cat.ContainsKey($k)) { $cat[$k] = 0 }
        $cat[$k]++
    }
}
$tot = 0
foreach ($k in $cat.Keys) {
    Write-Output "$k = $($cat[$k])"
    $tot += $cat[$k]
}
Write-Output "TOTAL = $tot"
