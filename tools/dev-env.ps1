# Windows-Pendant zu tools/dev-env.sh: laedt die Repo-Root-.env in die AKTUELLE PowerShell-Sitzung.
# Davor noetig fuer: dotnet ef, dotnet run --project server/src/Lumiis.Auth UND server/src/Lumiis.Server
# (docker compose liest .env im Repo-Root ohnehin selbst).
#
# WICHTIG: per Dot-Sourcing einbinden:   . .\tools\dev-env.ps1
# NICHT mit .\tools\dev-env.ps1 ausfuehren - dann gelten die Variablen nur im Skript-Scope.
# Datei bewusst ASCII-only (Windows PowerShell 5.1 liest UTF-8 ohne BOM sonst falsch).

if ($MyInvocation.InvocationName -ne '.') {
    Write-Error "dev-env.ps1 wurde ausgefuehrt statt eingebunden. Bitte '. .\tools\dev-env.ps1' verwenden (Punkt + Leerzeichen davor)."
    exit 1
}

$LumiisEnvFile = Join-Path (Split-Path -Parent $PSScriptRoot) '.env'

if (-not (Test-Path -LiteralPath $LumiisEnvFile)) {
    Write-Error "$LumiisEnvFile nicht gefunden. Erst 'Copy-Item .env.example .env' ausfuehren und eigene Werte eintragen."
    return
}

foreach ($LumiisLine in Get-Content -LiteralPath $LumiisEnvFile) {
    $LumiisTrimmed = $LumiisLine.Trim()
    if ($LumiisTrimmed -eq '' -or $LumiisTrimmed.StartsWith('#')) { continue }
    $LumiisEq = $LumiisTrimmed.IndexOf('=')
    if ($LumiisEq -lt 1) { continue }
    $LumiisName = $LumiisTrimmed.Substring(0, $LumiisEq).Trim()
    if ($LumiisName.StartsWith('export ')) { $LumiisName = $LumiisName.Substring(7).Trim() }
    $LumiisValue = $LumiisTrimmed.Substring($LumiisEq + 1).Trim()
    if ($LumiisValue.Length -ge 2 -and (($LumiisValue.StartsWith('"') -and $LumiisValue.EndsWith('"')) -or ($LumiisValue.StartsWith("'") -and $LumiisValue.EndsWith("'")))) {
        $LumiisValue = $LumiisValue.Substring(1, $LumiisValue.Length - 2)
    }
    Set-Item -Path "Env:$LumiisName" -Value $LumiisValue
}

Remove-Variable LumiisEnvFile, LumiisLine, LumiisTrimmed, LumiisEq, LumiisName, LumiisValue -ErrorAction SilentlyContinue
Write-Host "Umgebungsvariablen aus .env geladen (LUMIIS_DB_*, LUMIIS_TOKEN_SECRET, ...)."
