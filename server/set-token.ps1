# Кладёт токен бота на сервер. Токен вводите вы сами, он не показывается на экране и никуда не сохраняется на этом компьютере.
# Запуск: powershell -ExecutionPolicy Bypass -File set-token.ps1 -Server root@IP_СЕРВЕРА
param([Parameter(Mandatory = $true)][string]$Server)

$key = Join-Path $env:USERPROFILE ".ssh\discord_analyzer_vps"
Write-Host "Вставьте токен бота (Ctrl+V) и нажмите Enter. Символы видны не будут."
$sec = Read-Host -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

if ($plain.Trim().Length -lt 20) { Write-Host "Токен слишком короткий — похоже, он не вставился."; exit 1 }

$plain | & ssh -i $key -o StrictHostKeyChecking=accept-new $Server "install -o analyzer -g analyzer -m 600 /dev/stdin /opt/discord-analyzer/app/data/token.txt && systemctl restart discord-analyzer && echo TOKEN_OK"
Remove-Variable plain
if ($LASTEXITCODE -eq 0) { Write-Host "Готово: токен на сервере, бот перезапущен." } else { Write-Host "Не получилось. Проверьте адрес сервера и напишите Claude." }
