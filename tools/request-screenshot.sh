#!/usr/bin/env bash
# Fordert einen Screenshot aus einem BEREITS LAUFENDEN Unity-Play-Modus an - ohne Unity neu zu starten.
# Voraussetzung: der Client läuft im Editor/Development-Build (ScreenshotRequestWatcher ist aktiv).
#
# Aufruf: tools/request-screenshot.sh [Zielpfad]
# Default: /tmp/lumiis-screenshot.png

set -u

# Muss zum Temp-Ordner passen, den Unity per Path.GetTempPath() nutzt ($TMPDIR, sonst /tmp).
TMP_DIR="${TMPDIR:-/tmp}"
TMP_DIR="${TMP_DIR%/}"

TARGET="${1:-$TMP_DIR/lumiis-screenshot.png}"
REQUEST="$TMP_DIR/lumiis-screenshot-request"

rm -f "$TARGET"
printf '%s\n' "$TARGET" > "$REQUEST"

for _ in $(seq 1 100); do
    if [ -f "$TARGET" ]; then
        echo "Screenshot: $TARGET"
        exit 0
    fi
    sleep 0.1
done

echo "Kein Screenshot erhalten. Läuft Unity im Play-Modus?" >&2
echo "Alternativ im Spiel F12 drücken oder den Button im Asset-Testlabor nutzen." >&2
exit 1
