#!/usr/bin/env bash
# Führt die Unity-PlayMode-Tests (client/Assets/Lumiis/Tests/PlayMode/) im Batchmode aus, gibt den
# tatsächlichen Exit-Code von Unity weiter.
#
# WICHTIG: Der Unity-Editor muss geschlossen sein (Projekt-Lockfile), sonst schlägt der Batchmode-Aufruf fehl.
#
# Aufruf: tools/test-client-playmode.sh [Pfad-zum-Unity-Editor]
# Default-Editor-Pfad passt zum lokalen Setup; auf einem anderen Rechner ggf. als Argument überschreiben.

set -u

UNITY_BIN="${1:-$HOME/Unity/Hub/Editor/6000.3.24f1/Editor/Unity}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_FILE="/tmp/lumiis-test-client-playmode.log"
RESULTS_FILE="/tmp/lumiis-test-client-playmode-results.xml"

if [ ! -x "$UNITY_BIN" ]; then
    echo "Unity-Editor nicht gefunden unter: $UNITY_BIN" >&2
    echo "Pfad als erstes Argument übergeben, falls er auf diesem Rechner abweicht." >&2
    exit 1
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

echo "Führe PlayMode-Tests aus - Unity-Editor muss geschlossen sein ..."
echo "Log: $LOG_FILE"
echo "Ergebnisse: $RESULTS_FILE"

"$UNITY_BIN" -batchmode -nographics \
    -projectPath "$REPO_ROOT/client" \
    -runTests -testPlatform PlayMode \
    -testResults "$RESULTS_FILE" \
    -logFile "$LOG_FILE"
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    echo "PlayMode-Tests erfolgreich. Ergebnisse: $RESULTS_FILE"
else
    echo "PlayMode-Tests fehlgeschlagen (Exit-Code $EXIT_CODE). Log: $LOG_FILE, Ergebnisse: $RESULTS_FILE" >&2
fi

exit $EXIT_CODE
