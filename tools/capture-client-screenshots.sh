#!/usr/bin/env bash
# Erzeugt Screenshots des Asset-Testlabors direkt in Unity (Game View). Startet den Editor im
# GUI-Modus, betritt Play, lässt AssetLabAutoStart das Labor öffnen und PNGs schreiben, beendet
# den Editor danach automatisch. Nur für lokale Verifikation; benötigt ein GUI/Display.
#
# Aufruf: tools/capture-client-screenshots.sh [Pfad-zum-Unity-Editor]
# Ergebnis: /tmp/lumiis-asset-lab-plus0.png, /tmp/lumiis-asset-lab-plus9.png,
#           /tmp/lumiis-asset-lab-attack.png

set -u

UNITY_BIN="${1:-$HOME/Unity/Hub/Editor/6000.3.24f1/Editor/Unity}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_FILE="/tmp/lumiis-capture-client-screenshots.log"
MARKER="/tmp/lumiis-assetlab-done"

if [ ! -x "$UNITY_BIN" ]; then
    echo "Unity-Editor nicht gefunden unter: $UNITY_BIN" >&2
    echo "Pfad als erstes Argument übergeben, falls er auf diesem Rechner abweicht." >&2
    exit 1
fi

if [ -e "$REPO_ROOT/client/Temp/UnityLockfile" ]; then
    echo "Der Unity-Editor hat das Projekt geöffnet (Temp/UnityLockfile). Bitte schließen." >&2
    exit 1
fi

rm -f "$MARKER" /tmp/lumiis-asset-lab-plus0.png /tmp/lumiis-asset-lab-plus9.png /tmp/lumiis-asset-lab-attack.png
echo "Starte Unity für Asset-Testlabor-Screenshots ..."
echo "Log: $LOG_FILE"

RUN=( "$UNITY_BIN" -projectPath "$REPO_ROOT/client" -executeMethod Lumiis.Editor.AssetLabCaptureTool.Run -logFile "$LOG_FILE" -lumiisAssetLab )
if [ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null 2>&1; then
    xvfb-run -a "${RUN[@]}"
else
    "${RUN[@]}"
fi
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ]; then
    echo "Screenshot-Lauf fehlgeschlagen (Exit-Code $EXIT_CODE). Log: $LOG_FILE" >&2
    exit $EXIT_CODE
fi

for f in /tmp/lumiis-asset-lab-plus0.png /tmp/lumiis-asset-lab-plus9.png /tmp/lumiis-asset-lab-attack.png; do
    if [ ! -f "$f" ]; then
        echo "Screenshot fehlt: $f (Log: $LOG_FILE)" >&2
        exit 1
    fi
    echo "Screenshot: $f"
done
