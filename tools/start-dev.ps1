# Startet Lumiis.Auth und Lumiis.Server unter Windows in je einem eigenen PowerShell-Fenster, jeweils mit
# geladener .env (dev-env.ps1). Postgres muss bereits laufen (docker compose up -d oder lokaler Dienst).
#
# Aufruf: .\tools\start-dev.ps1 [-ServerArgs '--allow-debug-commands --start-map testfield']
# ASCII-only (Windows PowerShell 5.1).

param([string]$ServerArgs = '')

$RepoRoot = Split-Path -Parent $PSScriptRoot
$devEnv = Join-Path $PSScriptRoot 'dev-env.ps1'
$shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }

$authCmd = ". '$devEnv'; `$host.UI.RawUI.WindowTitle = 'Lumiis.Auth'; dotnet run --project server/src/Lumiis.Auth"
$serverCmd = ". '$devEnv'; `$host.UI.RawUI.WindowTitle = 'Lumiis.Server'; dotnet run --project server/src/Lumiis.Server"
if ($ServerArgs) { $serverCmd += " -- $ServerArgs" }

Start-Process -FilePath $shell -WorkingDirectory $RepoRoot -ArgumentList @('-NoExit', '-Command', $authCmd)
Start-Process -FilePath $shell -WorkingDirectory $RepoRoot -ArgumentList @('-NoExit', '-Command', $serverCmd)
Write-Host "Lumiis.Auth (Port 5080) und Lumiis.Server starten in eigenen Fenstern."
