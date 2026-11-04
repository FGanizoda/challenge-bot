# Удаление службы Windows. Файлы бота, базу и копии НЕ удаляет.
# Запускать в PowerShell от имени администратора:  .\deploy\uninstall-service.ps1

$ErrorActionPreference = "Stop"
$ServiceName = "ChallengeBot"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запусти PowerShell от имени администратора." }

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "Службы $ServiceName нет, делать нечего."; return }

if ($service.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName
    $service.WaitForStatus("Stopped", "00:00:30")
}
sc.exe delete $ServiceName | Out-Null
Write-Host "Служба удалена. Папка с базой и логами осталась на месте."
