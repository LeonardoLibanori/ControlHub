# test backslash file
$txt = 'a\b\c'
Write-Output $txt | Out-File -FilePath 'c:\Aplicações & Sistemas\Sistema de Estoque\ControlHub-limpo\_btest.txt' -Encoding UTF8
