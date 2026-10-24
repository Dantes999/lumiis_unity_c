#!/usr/bin/env bash
# Fordert einen Screenshot aus einem BEREITS LAUFENDEN Unity-Play-Modus an - ohne Unity neu zu starten.
# Voraussetzung: der Client läuft im Editor/Development-Build (ScreenshotRequestWatcher ist aktiv).
#
# Aufruf: tools/request-screenshot.sh [Zielpfad]
# Default: /tmp/lumiis-screenshot.png

set -u

TARGET="${1:-/tmp/lumiis-screenshot.png}"
REQUEST="/tmp/lumiis-screenshot-request"

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
