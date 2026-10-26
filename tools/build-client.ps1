# Windows-Pendant zu tools/build-client.sh: baut den Unity-Client als Development-Standalone-Build
# nach client\Builds\windows\Lumiis.exe.
#
# WICHTIG: Der Unity-Editor muss geschlossen sein (Projekt-Lockfile).
#
# Aufruf (PowerShell, Repo-Root):  .\tools\build-client.ps1 [-UnityPath <Pfad zu Unity.exe>]
# Danach starten:                  .\tools\run-client.ps1
# ASCII-only (Windows PowerShell 5.1).

param([string]$UnityPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'unity-common.ps1')

$UnityBin = Resolve-LumiisUnityEditor $UnityPath
Assert-LumiisProjectFree
Update-LumiisSharedDll

$LogFile = Join-Path $env:TEMP ("lumiis-build-client-{0}.log" -f (Split-Path -Leaf $RepoRoot))
Set-Content -LiteralPath $LogFile -Value '' -Encoding Ascii

Write-Host "Baue Client (Development, Windows 64 bit) aus $ProjectDir"
Write-Host "Log: $LogFile"

$unityArgs = @('-batchmode', '-nographics', '-quit',
    '-projectPath', "`"$ProjectDir`"",
    '-buildTarget', 'Win64',
    '-executeMethod', 'Lumiis.Editor.BuildScript.BuildDevelopment',
    '-logFile', "`"$LogFile`"")
$proc = Start-Process -FilePath $UnityBin -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
$null = $proc.Handle  # sonst liefert ExitCode nach dem Ende in PowerShell 5.1 manchmal $null

function Get-Phase([string]$log) {
    if ($log -match 'LUMIIS-BUILD: Player fertig') { return '4/4 Abschluss' }
    if ($log -match 'LUMIIS-BUILD: Player bauen') { return '3/4 Player bauen' }
    if ($log -match 'ScriptCompilation|Compiling|bee_backend|Assembly-CSharp|Lumiis.Client') { return '2/4 Skripte kompilieren' }
    return '1/4 Projekt laden/Assets importieren'
}
function Get-LastStep([string]$log) {
    $m = [regex]::Matches($log, '(DisplayProgressbar: [^\r\n]*|LUMIIS-BUILD: [^\r\n]*)')
    if ($m.Count -eq 0) { return '' }
    $s = $m[$m.Count - 1].Value -replace '^DisplayProgressbar: ', ''
    if ($s.Length -gt 60) { $s = $s.Substring(0, 60) }
    return $s
}

$start = Get-Date
try {
    while (-not $proc.HasExited) {
        $log = Read-SharedText $LogFile
        $elapsed = [int]((Get-Date) - $start).TotalSeconds
        $line = "[{0}] {1}  {2}" -f (Get-Phase $log), (Format-Duration $elapsed), (Get-LastStep $log)
        Write-Host ("`r" + $line.PadRight(110)) -NoNewline
        Start-Sleep -Seconds 1
    }
} finally {
    if (-not $proc.HasExited) { $proc.Kill(); Write-Host ''; Write-Host 'Abgebrochen.' }
}
Write-Host ''
$proc.WaitForExit()
$exitCode = $proc.ExitCode
$duration = Format-Duration ([int]((Get-Date) - $start).TotalSeconds)
$log = Read-SharedText $LogFile

if ($exitCode -eq 0) {
    Write-Host "Build erfolgreich nach $duration min."
    $m = [regex]::Match($log, 'LUMIIS-BUILD: Player fertig -> ([^\r\n]*)')
    if ($m.Success) { Write-Host ("Exe: " + (Join-Path $ProjectDir $m.Groups[1].Value.Trim())) }
    Write-Host "Starten mit: .\tools\run-client.ps1"
} else {
    Write-Host "Build fehlgeschlagen nach $duration min (Exit-Code $exitCode). Relevante Logzeilen:" -ForegroundColor Red
    ($log -split "`r?`n") | Where-Object { $_ -match 'error|Error|LUMIIS-BUILD|another Unity instance' } |
        Select-Object -Last 15 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "Vollstaendiges Log: $LogFile" -ForegroundColor Red
}
exit $exitCode
