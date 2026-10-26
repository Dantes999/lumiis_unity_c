# Gemeinsame Helfer fuer die Windows-Unity-Skripte (build-client.ps1, test-client-playmode.ps1).
# Wird per Dot-Sourcing eingebunden, nicht direkt aufrufen. ASCII-only (Windows PowerShell 5.1).

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $RepoRoot 'client'

# Unity-Version aus ProjectVersion.txt, damit der Default-Pfad bei einem Editor-Update mitwandert.
function Get-LumiisUnityVersion {
    $file = Join-Path $ProjectDir 'ProjectSettings\ProjectVersion.txt'
    $line = Select-String -LiteralPath $file -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
    if ($line) { return $line.Matches[0].Groups[1].Value }
    return '6000.3.24f1'
}

function Resolve-LumiisUnityEditor([string]$explicitPath) {
    if ($explicitPath) { $candidates = @($explicitPath) }
    else {
        $version = Get-LumiisUnityVersion
        $candidates = @(
            (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"),
            "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
            "D:\Unity\Hub\Editor\$version\Editor\Unity.exe"
        )
    }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return (Resolve-Path -LiteralPath $c).Path }
    }
    Write-Host "Unity-Editor nicht gefunden unter: $($candidates -join ', ')" -ForegroundColor Red
    Write-Host "Pfad als Parameter uebergeben, z. B. -UnityPath 'E:\Unity\6000.3.24f1\Editor\Unity.exe'." -ForegroundColor Red
    exit 1
}

# Unity-Prozesse, die genau dieses Projekt offen haben (Editor oder Batchmode).
function Get-LumiisUnityProcesses {
    $needle = $ProjectDir.ToLowerInvariant()
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.ToLowerInvariant().Replace('/', '\').Contains($needle) }
}

function Assert-LumiisProjectFree {
    $running = @(Get-LumiisUnityProcesses)
    if ($running.Count -gt 0) {
        Write-Host "Ein Unity-Prozess hat dieses Projekt bereits offen (Editor oder anderer Build):" -ForegroundColor Red
        $running | ForEach-Object { Write-Host ("  PID {0}: {1}" -f $_.ProcessId, $_.CommandLine) -ForegroundColor Red }
        Write-Host "Editor schliessen bzw. den laufenden Build abwarten." -ForegroundColor Red
        exit 1
    }
    $lock = Join-Path $ProjectDir 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lock) {
        try {
            Remove-Item -LiteralPath $lock -Force -ErrorAction Stop
            Write-Host "Verwaiste Lockfile ohne Unity-Prozess entfernt: $lock"
        } catch {
            Write-Host "Lockfile $lock ist noch belegt - laeuft Unity doch noch?" -ForegroundColor Red
            exit 1
        }
    }
}

# Lumiis.Shared.dll kommt per dotnet build nach client/Assets/Plugins/Lumiis/ - ohne diesen Schritt
# kompiliert Unity nach einem git pull gegen eine veraltete DLL ("type ... could not be found").
function Update-LumiisSharedDll {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "Warnung: dotnet nicht gefunden - Lumiis.Shared.dll wird nicht aktualisiert." -ForegroundColor Yellow
        return
    }
    Write-Host "Aktualisiere Lumiis.Shared.dll (dotnet build) ..."
    $log = Join-Path $env:TEMP 'lumiis-shared-build.log'
    # Stderr eines nativen Befehls wuerde unter 'Stop' in PowerShell 5.1 sonst als Fehler abbrechen.
    $ErrorActionPreference = 'Continue'
    & dotnet build (Join-Path $RepoRoot 'server\src\Lumiis.Shared\Lumiis.Shared.csproj') -nologo -v q *> $log
    if ($LASTEXITCODE -ne 0) {
        Write-Host "dotnet build von Lumiis.Shared fehlgeschlagen, siehe $log" -ForegroundColor Red
        exit 1
    }
}

# Liest eine Datei, die Unity gerade noch beschreibt (FileShare.ReadWrite statt Get-Content-Sperre).
function Read-SharedText([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
        try { return (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
    } catch { return '' }
}

function Format-Duration([int]$seconds) { return ('{0}:{1:00}' -f [int][math]::Floor($seconds / 60), ($seconds % 60)) }
