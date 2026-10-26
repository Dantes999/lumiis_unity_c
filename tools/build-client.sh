#!/usr/bin/env bash
# Baut den Unity-Client als Development-Standalone-Build für die aktuelle Plattform nach client/Builds/<plattform>/.
#
# WICHTIG: Der Unity-Editor muss geschlossen sein (Projekt-Lockfile), sonst schlägt der Batchmode-Aufruf fehl.
#
# Aufruf: tools/build-client.sh [Pfad-zum-Unity-Editor]
# Default-Editor-Pfad passt zum lokalen Setup; auf einem anderen Rechner ggf. als Argument überschreiben.
#
# Während des Builds zeigt eine Statuszeile Phase, vergangene Zeit und den letzten Unity-Fortschrittsschritt.
# Das Log liegt je Checkout getrennt unter /tmp (zwei gleichzeitige Builds aus verschiedenen Ordnern
# überschreiben sich nicht mehr gegenseitig).

set -u

UNITY_BIN="${1:-$HOME/Unity/Hub/Editor/6000.3.24f1/Editor/Unity}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_DIR="$REPO_ROOT/client"
LOG_FILE="/tmp/lumiis-build-client-$(basename "$REPO_ROOT").log"
LOCK_FILE="$PROJECT_DIR/Temp/UnityLockfile"

if [ ! -x "$UNITY_BIN" ]; then
    echo "Unity-Editor nicht gefunden unter: $UNITY_BIN" >&2
    echo "Pfad als erstes Argument übergeben, falls er auf diesem Rechner abweicht." >&2
    exit 1
fi

# Läuft schon ein Unity-Prozess auf genau diesem Projekt? Dann gleich mit klarer Meldung abbrechen.
RUNNING="$(pgrep -af -- "-projectPath $PROJECT_DIR( |\$)" | grep -iv pgrep || true)"
if [ -n "$RUNNING" ]; then
    echo "Ein Unity-Prozess hat dieses Projekt bereits offen (Editor oder anderer Build):" >&2
    echo "$RUNNING" >&2
    echo "Editor schließen bzw. den laufenden Build abwarten." >&2
    exit 1
fi
if [ -e "$LOCK_FILE" ] && command -v fuser >/dev/null 2>&1 && ! fuser "$LOCK_FILE" >/dev/null 2>&1; then
    echo "Verwaiste Lockfile ohne Unity-Prozess gefunden, wird entfernt: $LOCK_FILE"
    rm -f "$LOCK_FILE"
fi

# Lumiis.Shared.dll (Pakete wie MapEventInfo) kommt per dotnet build nach client/Assets/Plugins/Lumiis/ - ohne
# diesen Schritt kompiliert Unity nach einem git pull gegen eine veraltete DLL ("type ... could not be found").
if command -v dotnet >/dev/null 2>&1; then
    echo "Aktualisiere Lumiis.Shared.dll (dotnet build) ..."
    if ! dotnet build "$REPO_ROOT/server/src/Lumiis.Shared/Lumiis.Shared.csproj" -nologo -v q >/tmp/lumiis-shared-build.log 2>&1; then
        echo "dotnet build von Lumiis.Shared fehlgeschlagen, siehe /tmp/lumiis-shared-build.log" >&2
        exit 1
    fi
else
    echo "Warnung: dotnet nicht gefunden - Lumiis.Shared.dll wird nicht aktualisiert." >&2
fi

echo "Baue Client (Development, aktuelle Plattform) aus $PROJECT_DIR"
echo "Log: $LOG_FILE"
: > "$LOG_FILE"

"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$PROJECT_DIR" \
    -executeMethod Lumiis.Editor.BuildScript.BuildDevelopment \
    -logFile "$LOG_FILE" &
UNITY_PID=$!
trap 'kill "$UNITY_PID" 2>/dev/null; echo; echo "Abgebrochen."; exit 130' INT TERM

START=$(date +%s)

# Phase anhand von Markern im Log (BuildScript schreibt eigene "LUMIIS-BUILD:"-Zeilen).
phase() {
    if grep -q "LUMIIS-BUILD: Player fertig" "$LOG_FILE"; then echo "4/4 Abschluss"
    elif grep -q "LUMIIS-BUILD: Player bauen" "$LOG_FILE"; then echo "3/4 Player bauen"
    elif grep -qE "ScriptCompilation|Compiling|bee_backend|Assembly-CSharp|Lumiis.Client" "$LOG_FILE"; then echo "2/4 Skripte kompilieren"
    else echo "1/4 Projekt laden/Assets importieren"
    fi
}
last_step() {
    grep -aoE "DisplayProgressbar: .*|LUMIIS-BUILD: .*" "$LOG_FILE" | tail -1 | sed -E 's/^DisplayProgressbar: //' | cut -c1-60
}

if [ -t 1 ]; then
    while kill -0 "$UNITY_PID" 2>/dev/null; do
        ELAPSED=$(( $(date +%s) - START ))
        printf '\r\033[K[%s] %02d:%02d  %s' "$(phase)" $((ELAPSED / 60)) $((ELAPSED % 60)) "$(last_step)"
        sleep 1
    done
    echo
else
    # Kein Terminal (z. B. Ausgabe in eine Datei): Phasenwechsel als eigene Zeilen.
    LAST=""
    while kill -0 "$UNITY_PID" 2>/dev/null; do
        P="$(phase)"
        if [ "$P" != "$LAST" ]; then
            ELAPSED=$(( $(date +%s) - START ))
            echo "[$P] nach $((ELAPSED / 60)):$(printf '%02d' $((ELAPSED % 60)))"
            LAST="$P"
        fi
        sleep 2
    done
fi

wait "$UNITY_PID"
EXIT_CODE=$?
ELAPSED=$(( $(date +%s) - START ))
DURATION="$((ELAPSED / 60)):$(printf '%02d' $((ELAPSED % 60)))"

if [ $EXIT_CODE -eq 0 ]; then
    OUTPUT="$(grep -aoE "LUMIIS-BUILD: Player fertig -> .*" "$LOG_FILE" | tail -1 | sed -E 's/.*-> //')"
    echo "Build erfolgreich nach $DURATION min."
    [ -n "$OUTPUT" ] && echo "Starten mit: $PROJECT_DIR/$OUTPUT -logFile -"
else
    echo "Build fehlgeschlagen nach $DURATION min (Exit-Code $EXIT_CODE). Relevante Logzeilen:" >&2
    grep -aE "error|Error|LUMIIS-BUILD|another Unity instance" "$LOG_FILE" | grep -v "^\s*$" | tail -15 >&2
    echo "Vollständiges Log: $LOG_FILE" >&2
fi

exit $EXIT_CODE
