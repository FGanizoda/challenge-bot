# Установка (или обновление) бота как службы Windows с автозапуском и перезапуском при падении.
#
# Как запускать: PowerShell ОТ ИМЕНИ АДМИНИСТРАТОРА, из папки проекта:
#     .\deploy\install-service.ps1
# Бот будет лежать в C:\ChallengeBot (другая папка: -InstallDir "D:\Bots\Challenge").
# Токен скрипт не трогает: положи его сам (см. README, шаг 1).

param([string]$InstallDir = "C:\ChallengeBot")

$ErrorActionPreference = "Stop"
$ServiceName = "ChallengeBot"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запусти PowerShell от имени администратора (правой кнопкой по значку, Запуск от имени администратора)." }

$projectRoot = Split-Path -Parent $PSScriptRoot

# Если служба уже есть и работает, останавливаем её, чтобы заменить файлы.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne "Stopped") {
    Write-Host "Останавливаю службу..."
    Stop-Service -Name $ServiceName
    $existing.WaitForStatus("Stopped", "00:00:30")
}

Write-Host "Собираю релиз в $InstallDir ..."
dotnet publish "$projectRoot\ChallengeBot" -c Release -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой." }

if (-not $existing) {
    Write-Host "Создаю службу $ServiceName ..."
    sc.exe create $ServiceName binPath= "`"$InstallDir\ChallengeBot.exe`"" start= auto DisplayName= "Challenge Bot" | Out-Null
    sc.exe description $ServiceName "Telegram-бот для 60-дневного челленджа по здоровью спины и суставов" | Out-Null
}

# Перезапуск при падении: через 5 с, ещё раз через 5 с, потом через 30 с; счётчик сбрасывается раз в сутки.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

$hasToken = (Test-Path "$InstallDir\appsettings.Local.json") -or
            [Environment]::GetEnvironmentVariable("BOT_TOKEN", "Machine")
if (-not $hasToken) {
    Write-Warning "Токен не найден. Положи appsettings.Local.json в $InstallDir (образец: ChallengeBot\appsettings.Local.example.json), потом выполни: Start-Service $ServiceName"
    return
}

Write-Host "Запускаю службу..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-Table Name, Status, StartType -AutoSize
Write-Host "Готово. Логи: $InstallDir\logs, копии базы: $InstallDir\backups"
