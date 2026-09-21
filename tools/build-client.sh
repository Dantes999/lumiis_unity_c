#!/usr/bin/env bash
# Baut den Unity-Client als Development-Standalone-Build für die aktuelle Plattform nach client/Builds/<plattform>/.
#
# WICHTIG: Der Unity-Editor muss geschlossen sein (Projekt-Lockfile), sonst schlägt der Batchmode-Aufruf fehl.
#
# Aufruf: tools/build-client.sh [Pfad-zum-Unity-Editor]
# Default-Editor-Pfad passt zum lokalen Setup; auf einem anderen Rechner ggf. als Argument überschreiben.

set -u

UNITY_BIN="${1:-$HOME/Unity/Hub/Editor/6000.3.24f1/Editor/Unity}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_FILE="/tmp/lumiis-build-client.log"

if [ ! -x "$UNITY_BIN" ]; then
    echo "Unity-Editor nicht gefunden unter: $UNITY_BIN" >&2
    echo "Pfad als erstes Argument übergeben, falls er auf diesem Rechner abweicht." >&2
    exit 1
fi

echo "Baue Client (Development, aktuelle Plattform) - Unity-Editor muss geschlossen sein ..."
echo "Log: $LOG_FILE"

"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$REPO_ROOT/client" \
    -executeMethod Lumiis.Editor.BuildScript.BuildDevelopment \
    -logFile "$LOG_FILE"
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    echo "Build erfolgreich. Log: $LOG_FILE"
else
    echo "Build fehlgeschlagen (Exit-Code $EXIT_CODE). Log: $LOG_FILE" >&2
fi

exit $EXIT_CODE
