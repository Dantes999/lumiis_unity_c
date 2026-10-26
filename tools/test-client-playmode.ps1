# Windows-Pendant zu tools/test-client-playmode.sh: fuehrt die Unity-PlayMode-Tests im Batchmode aus und
# gibt den Exit-Code von Unity weiter.
#
# WICHTIG: Der Unity-Editor muss geschlossen sein (Projekt-Lockfile).
#
# Aufruf: .\tools\test-client-playmode.ps1 [-UnityPath <Pfad zu Unity.exe>]
# ASCII-only (Windows PowerShell 5.1).

param([string]$UnityPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'unity-common.ps1')

$UnityBin = Resolve-LumiisUnityEditor $UnityPath
Assert-LumiisProjectFree
Update-LumiisSharedDll

$LogFile = Join-Path $env:TEMP 'lumiis-test-client-playmode.log'
$ResultsFile = Join-Path $env:TEMP 'lumiis-test-client-playmode-results.xml'
Write-Host "Fuehre PlayMode-Tests aus - Unity-Editor muss geschlossen sein ..."
Write-Host "Log: $LogFile"
Write-Host "Ergebnisse: $ResultsFile"

$unityArgs = @('-batchmode', '-nographics',
    '-projectPath', "`"$ProjectDir`"",
    '-runTests', '-testPlatform', 'PlayMode',
    '-testResults', "`"$ResultsFile`"",
    '-logFile', "`"$LogFile`"")
$proc = Start-Process -FilePath $UnityBin -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
$null = $proc.Handle
$proc.WaitForExit()
$exitCode = $proc.ExitCode

if ($exitCode -eq 0) {
    Write-Host "PlayMode-Tests erfolgreich. Ergebnisse: $ResultsFile"
} else {
    Write-Host "PlayMode-Tests fehlgeschlagen (Exit-Code $exitCode). Log: $LogFile, Ergebnisse: $ResultsFile" -ForegroundColor Red
}
exit $exitCode
