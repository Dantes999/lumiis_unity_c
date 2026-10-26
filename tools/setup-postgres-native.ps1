# Nur fuer Windows OHNE Docker: legt in einem lokal installierten PostgreSQL (EDB-Installer, Version 16)
# den Benutzer und die Datenbank aus .env an (LUMIIS_DB_USER/PASSWORD/NAME). Mehrfach ausfuehrbar - ein
# vorhandener Benutzer bekommt nur das Passwort neu gesetzt, eine vorhandene Datenbank bleibt unangetastet.
#
# Aufruf: .\tools\setup-postgres-native.ps1 [-PsqlPath <Pfad zu psql.exe>]
# Fragt einmal nach dem Passwort des Superusers "postgres" (das beim Installieren vergebene).
# Die Tabellen legt danach Lumiis.Auth beim ersten Start selbst an (Migration in Development).
# ASCII-only (Windows PowerShell 5.1).

param([string]$PsqlPath)

. (Join-Path $PSScriptRoot 'dev-env.ps1')
foreach ($n in 'LUMIIS_DB_NAME', 'LUMIIS_DB_USER', 'LUMIIS_DB_PASSWORD') {
    if (-not (Get-Item "Env:$n" -ErrorAction SilentlyContinue)) { Write-Host "$n fehlt in .env." -ForegroundColor Red; exit 1 }
}

if (-not $PsqlPath) {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($cmd) { $PsqlPath = $cmd.Source }
    else {
        $PsqlPath = Get-ChildItem "$env:ProgramFiles\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $PsqlPath -or -not (Test-Path -LiteralPath $PsqlPath)) {
    Write-Host "psql.exe nicht gefunden. PostgreSQL installieren oder -PsqlPath angeben." -ForegroundColor Red
    exit 1
}

$dbHost = if ($env:LUMIIS_DB_HOST) { $env:LUMIIS_DB_HOST } else { 'localhost' }
$dbPort = if ($env:LUMIIS_DB_PORT) { $env:LUMIIS_DB_PORT } else { '5432' }

$secure = Read-Host -AsSecureString "Passwort des PostgreSQL-Superusers 'postgres'"
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

function Invoke-Psql([string]$sql) {
    $out = & $PsqlPath -h $dbHost -p $dbPort -U postgres -d postgres -v ON_ERROR_STOP=1 -tAc $sql
    if ($LASTEXITCODE -ne 0) { throw "psql fehlgeschlagen: $sql" }
    return $out
}

try {
    $user = $env:LUMIIS_DB_USER
    $db = $env:LUMIIS_DB_NAME
    $pw = $env:LUMIIS_DB_PASSWORD.Replace("'", "''")
    $userQ = '"' + $user.Replace('"', '""') + '"'
    $dbQ = '"' + $db.Replace('"', '""') + '"'

    $hasUser = Invoke-Psql ("SELECT 1 FROM pg_roles WHERE rolname = '" + $user.Replace("'", "''") + "'")
    if ($hasUser -eq '1') {
        Invoke-Psql "ALTER ROLE $userQ WITH LOGIN PASSWORD '$pw'" | Out-Null
        Write-Host "Benutzer $user existiert, Passwort aus .env gesetzt."
    } else {
        Invoke-Psql "CREATE ROLE $userQ WITH LOGIN PASSWORD '$pw'" | Out-Null
        Write-Host "Benutzer $user angelegt."
    }

    $hasDb = Invoke-Psql ("SELECT 1 FROM pg_database WHERE datname = '" + $db.Replace("'", "''") + "'")
    if ($hasDb -eq '1') {
        Write-Host "Datenbank $db existiert bereits."
    } else {
        Invoke-Psql "CREATE DATABASE $dbQ OWNER $userQ" | Out-Null
        Write-Host "Datenbank $db angelegt (Besitzer $user)."
    }
    Write-Host "Fertig. Jetzt .\tools\start-dev.ps1 (Lumiis.Auth migriert beim ersten Start)."
} catch {
    Write-Host $_ -ForegroundColor Red
    exit 1
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
