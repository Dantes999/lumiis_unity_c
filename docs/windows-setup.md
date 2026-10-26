# Lumiis auf Windows einrichten

Anleitung für den zweiten Entwicklungsrechner (Windows 10/11). Alles läuft wie unter Linux, nur die
Shell-Skripte gibt es zusätzlich als PowerShell-Variante (`tools/*.ps1`). Die Linux-Skripte bleiben
unverändert gültig.

## 1. Programme installieren

| Programm | Woher | Hinweis |
|---|---|---|
| Git for Windows | https://git-scm.com/download/win oder `winget install Git.Git` | bringt auch **Git Bash** mit (für `tools/check-repo.sh` und andere `.sh`-Skripte) |
| .NET 10 SDK | https://dotnet.microsoft.com/download/dotnet/10.0 oder `winget install Microsoft.DotNet.SDK.10` | danach `dotnet --version` prüfen, muss mit `10.` beginnen |
| Unity Hub + Editor **6000.3.24f1** | https://unity.com/download, im Hub unter "Installs" genau diese Version | Modul "Windows Build Support (Mono)" ist beim Windows-Editor dabei; optional "Visual Studio" oder Rider als Code-Editor |
| Datenbank | siehe Abschnitt 4 | Docker Desktop **oder** PostgreSQL 16 |
| Claude Desktop-App | https://claude.ai/download | anmelden, dann kann Claude im Projekt auf den Repo-Ordner des Laptops zugreifen |
| Python 3 (optional) | `winget install Python.Python.3.12` | nur für Werkzeuge wie `tools/bake-collision.py` (`pip install pillow`) |

Ein Unity-Lizenz-Login im Hub ist nötig (Personal reicht), sonst laufen auch die Batchmode-Builds nicht.

## 2. Einmalige Einstellungen

PowerShell als normaler Benutzer öffnen:

```powershell
# Eigene Skripte aus dem Repo erlauben (Standard ist "Restricted")
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned

# Git: Zeilenenden nicht umwandeln (das Repo erzwingt LF über .gitattributes), lange Pfade erlauben
git config --global core.autocrlf false
git config --global core.longpaths true
git config --global user.name "Dein Name"
git config --global user.email "deine@mail"
```

## 3. Repo klonen

Einen kurzen Pfad **außerhalb von OneDrive** wählen (OneDrive-Synchronisierung von "Desktop"/"Dokumente"
verträgt sich nicht mit Unitys `Library/`-Ordner):

```powershell
mkdir C:\dev
cd C:\dev
git clone https://github.com/Dantes999/lumiis_unity_c.git
cd lumiis_unity_c
```

Danach `.env` anlegen und ausfüllen:

```powershell
Copy-Item .env.example .env
notepad .env
```

`LUMIIS_DB_PASSWORD` frei wählen. Für `LUMIIS_TOKEN_SECRET` einen Zufallswert erzeugen:

```powershell
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

`.env` ist git-ignoriert und wird nie committet.

## 4. Datenbank (PostgreSQL 16)

**Empfehlung: Docker Desktop**, dann ist alles identisch zum Linux-Rechner.

### Variante A: Docker Desktop (empfohlen)

1. Docker Desktop installieren (https://www.docker.com/products/docker-desktop/, nutzt WSL 2; beim ersten
   Start ggf. Neustart und `wsl --update`).
2. Im Repo-Root:

   ```powershell
   docker compose up -d
   ```

   `docker compose` liest die `.env` im Repo-Root selbst, `dev-env.ps1` ist dafür nicht nötig.

### Variante B: PostgreSQL direkt installieren (ohne Docker)

1. Installer von https://www.postgresql.org/download/windows/ (Version 16), Port 5432, beim Installieren ein
   Passwort für den Superuser `postgres` vergeben. pgAdmin/Stack Builder sind nicht nötig.
2. Benutzer und Datenbank aus `.env` anlegen (fragt einmal nach dem `postgres`-Passwort):

   ```powershell
   .\tools\setup-postgres-native.ps1
   ```

Nicht beide Varianten gleichzeitig auf Port 5432 laufen lassen.

Die Tabellen legt `Lumiis.Auth` beim Start selbst an (automatische Migration, weil `launchSettings.json`
`ASPNETCORE_ENVIRONMENT=Development` setzt). Manuell geht es auch:

```powershell
. .\tools\dev-env.ps1
cd server; dotnet tool restore; cd src\Lumiis.Auth
dotnet ef database update --project ..\Lumiis.Data
cd ..\..\..
```

## 5. Server bauen und testen

```powershell
cd server
dotnet build
dotnet test
cd ..
```

`dotnet build` kopiert dabei auch `Lumiis.Shared.dll`/`LiteNetLib.dll` nach `client\Assets\Plugins\Lumiis\`
und die Spieldaten nach `client\Assets\StreamingAssets\lumiis-data\` - das muss **vor** dem ersten Öffnen
in Unity passiert sein. `dotnet test` braucht kein laufendes Postgres.

## 6. Auth und Server starten

Bequem, je ein eigenes Fenster mit geladener `.env`:

```powershell
.\tools\start-dev.ps1
# mit Dev-Optionen:
.\tools\start-dev.ps1 -ServerArgs '--allow-debug-commands --start-map testfield'
```

Oder von Hand, je Fenster (Punkt + Leerzeichen am Anfang ist wichtig, das ist das Gegenstück zu
`source tools/dev-env.sh`):

```powershell
. .\tools\dev-env.ps1
dotnet run --project server/src/Lumiis.Auth      # Fenster 1, Port 5080
dotnet run --project server/src/Lumiis.Server    # Fenster 2
```

Beim ersten Start fragt die Windows-Firewall nach `dotnet.exe`: für **private** Netzwerke erlauben.

Bots: `dotnet run --project server/src/Lumiis.Bot -- --count 3`

## 7. Client in Unity

1. Unity Hub -> "Add" -> "Add project from disk" -> `C:\dev\lumiis_unity_c\client`.
2. Öffnen (der erste Import dauert ein paar Minuten, `Library\` entsteht lokal und bleibt git-ignoriert).
3. Play drücken, anmelden/registrieren, Charakter wählen.

Nach jedem `git pull`, der Server-/Shared-Code ändert, erneut `cd server; dotnet build` - sonst kompiliert
Unity gegen eine veraltete `Lumiis.Shared.dll`.

## 8. Standalone-Build (schneller als der Editor)

Unity-Editor vorher schließen (Projekt-Lock):

```powershell
.\tools\build-client.ps1     # baut client\Builds\windows\Lumiis.exe, zeigt Phase + Zeit
.\tools\run-client.ps1       # startet den Build und zeigt dessen Log live im Fenster
```

Liegt Unity nicht unter `C:\Program Files\Unity\Hub\Editor\<Version>\`, den Pfad mitgeben:
`.\tools\build-client.ps1 -UnityPath 'E:\Unity\6000.3.24f1\Editor\Unity.exe'`.

PlayMode-Tests: `.\tools\test-client-playmode.ps1` (Editor ebenfalls geschlossen).

Screenshots (F12 im Spiel / Asset-Testlabor) landen unter Windows in `%TEMP%` statt `/tmp`.

## 9. Skript-Übersicht Linux -> Windows

| Linux (bash) | Windows (PowerShell) |
|---|---|
| `source tools/dev-env.sh` | `. .\tools\dev-env.ps1` |
| `docker compose up -d` | gleich (oder Variante B oben) |
| `tools/build-client.sh` | `.\tools\build-client.ps1` |
| `client/Builds/linux/Lumiis -logFile -` | `.\tools\run-client.ps1` |
| `tools/test-client-playmode.sh` | `.\tools\test-client-playmode.ps1` |
| Auth + Server in zwei Terminals | `.\tools\start-dev.ps1` |
| `./tools/check-repo.sh` | in **Git Bash**: `./tools/check-repo.sh` |

Übrige `.sh`-Skripte (Screenshots, Python-Werkzeuge) laufen bei Bedarf ebenfalls in Git Bash.

## 10. Arbeiten mit zwei Rechnern

- Immer direkt auf `main`: vor dem Arbeiten `git pull --rebase`, vor jedem Push `git fetch` +
  `git rebase origin/main`, dann `./tools/check-repo.sh` in Git Bash.
- Dateinamen exakt schreiben: Windows ignoriert Groß-/Kleinschreibung, Linux nicht. Eine Umbenennung nur
  der Schreibweise mit `git mv alt.cs Alt.cs` machen, nie im Explorer.
- Unity verändert beim Öffnen gern `client/ProjectSettings/ProjectSettings.asset` und Dateien unter
  `client/Assets/Resources/` (PerformanceTestRunInfo/-Settings). Solche Änderungen nicht committen, sondern
  mit `git restore <datei>` verwerfen, außer man hat bewusst etwas umgestellt.
- Nie `git add -A` im Unity-Ordner; vorher `git status --short` und `git diff --cached --stat` prüfen.
- Die `tools/*.ps1` sind bewusst ASCII-only (keine Umlaute), damit sie auch in der alten Windows
  PowerShell 5.1 ohne Kodierungsprobleme laufen.
