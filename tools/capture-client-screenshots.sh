#!/usr/bin/env bash
# Erzeugt Screenshots des Asset-Testlabors direkt in Unity (Game View). Startet den Editor im
# GUI-Modus, betritt Play, lässt AssetLabAutoStart das Labor öffnen und PNGs schreiben, beendet
# den Editor danach automatisch. Nur für lokale Verifikation; benötigt ein GUI/Display.
#
# Aufruf: tools/capture-client-screenshots.sh [Pfad-zum-Unity-Editor]
# Ergebnis: /tmp/lumiis-asset-lab-plus0.png, /tmp/lumiis-asset-lab-plus9.png,
#           /tmp/lumiis-asset-lab-attack.png

set -u

# Muss zum Temp-Ordner passen, den Unity per Path.GetTempPath() nutzt ($TMPDIR, sonst /tmp).
TMP_DIR="${TMPDIR:-/tmp}"
TMP_DIR="${TMP_DIR%/}"

UNITY_BIN="${1:-$HOME/Unity/Hub/Editor/6000.3.24f1/Editor/Unity}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_FILE="$TMP_DIR/lumiis-capture-client-screenshots.log"
MARKER="$TMP_DIR/lumiis-assetlab-done"

if [ ! -x "$UNITY_BIN" ]; then
    echo "Unity-Editor nicht gefunden unter: $UNITY_BIN" >&2
    echo "Pfad als erstes Argument übergeben, falls er auf diesem Rechner abweicht." >&2
    exit 1
fi

if [ -e "$REPO_ROOT/client/Temp/UnityLockfile" ]; then
    echo "Der Unity-Editor hat das Projekt geöffnet (Temp/UnityLockfile). Bitte schließen." >&2
    exit 1
fi

rm -f "$MARKER" $TMP_DIR/lumiis-asset-lab-plus0.png $TMP_DIR/lumiis-asset-lab-plus9.png $TMP_DIR/lumiis-asset-lab-attack.png
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

for f in $TMP_DIR/lumiis-asset-lab-plus0.png $TMP_DIR/lumiis-asset-lab-plus9.png $TMP_DIR/lumiis-asset-lab-attack.png $TMP_DIR/lumiis-asset-lab-icons.png $TMP_DIR/lumiis-asset-lab-map.png $TMP_DIR/lumiis-asset-lab-characters.png $TMP_DIR/lumiis-hud.png; do
    if [ ! -f "$f" ]; then
        echo "Screenshot fehlt: $f (Log: $LOG_FILE)" >&2
        exit 1
    fi
    echo "Screenshot: $f"
done
