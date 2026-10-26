# Startet den mit build-client.ps1 gebauten Windows-Client und zeigt sein Log live in diesem Fenster
# (Windows-Ersatz fuer "Lumiis -logFile -", das bei einer Windows-GUI-Exe nichts in die Konsole schreibt).
# Ende mit Schliessen des Spielfensters (oder Strg+C beendet nur die Log-Anzeige).
#
# Aufruf: .\tools\run-client.ps1 [weitere Unity-Player-Argumente]
# ASCII-only (Windows PowerShell 5.1).

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $RepoRoot 'client\Builds\windows\Lumiis.exe'
if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host "Kein Build gefunden: $Exe - erst .\tools\build-client.ps1 ausfuehren." -ForegroundColor Red
    exit 1
}

$LogFile = Join-Path $env:TEMP 'lumiis-player.log'
Set-Content -LiteralPath $LogFile -Value '' -Encoding Ascii
$playerArgs = @('-logFile', "`"$LogFile`"") + $args
$proc = Start-Process -FilePath $Exe -ArgumentList $playerArgs -PassThru
Write-Host "Client gestartet (PID $($proc.Id)). Log: $LogFile"

$pos = 0L
while ($true) {
    if (Test-Path -LiteralPath $LogFile) {
        try {
            $fs = [System.IO.File]::Open($LogFile, 'Open', 'Read', 'ReadWrite')
            try {
                if ($fs.Length -lt $pos) { $pos = 0 }
                $null = $fs.Seek($pos, 'Begin')
                $text = (New-Object System.IO.StreamReader($fs)).ReadToEnd()
                $pos = $fs.Length
            } finally { $fs.Dispose() }
            if ($text) { Write-Host $text -NoNewline }
        } catch { }
    }
    if ($proc.HasExited) { break }
    Start-Sleep -Milliseconds 300
}
Write-Host ''
Write-Host "Client beendet (Exit-Code $($proc.ExitCode))."
