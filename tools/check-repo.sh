#!/usr/bin/env bash
# Prüft den Zustand des Repos auf die Fehlerklasse "generische Gitignore-Regel verschluckt
# Unity-Dateien" (siehe CLAUDE.md-Konvention "Ignore-Regeln pro Teilbaum"). Nicht destruktiv,
# meldet nur. Exit-Code 0 = alles sauber, sonst != 0.
#
# Aufruf: tools/check-repo.sh (vor jedem Push, siehe CLAUDE.md)

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1

FAILED=0

fail() {
    echo "FEHLER: $1" >&2
    FAILED=1
}

echo "== (i) Pflichtdateien getrackt =="
REQUIRED_FILES=(
    "client/ProjectSettings/ProjectVersion.txt"
    "client/Packages/manifest.json"
    "client/Packages/packages-lock.json"
)
for f in "${REQUIRED_FILES[@]}"; do
    if ! git ls-files --error-unmatch "$f" >/dev/null 2>&1; then
        fail "Pflichtdatei nicht getrackt: $f"
    fi
done

# Alle *.asset unter ProjectSettings/ müssen getrackt sein.
while IFS= read -r -d '' f; do
    rel="${f#./}"
    if ! git ls-files --error-unmatch "$rel" >/dev/null 2>&1; then
        fail "Pflichtdatei nicht getrackt: $rel"
    fi
done < <(find client/ProjectSettings -maxdepth 1 -name "*.asset" -print0 2>/dev/null)

# Alle Dateien unter Assets/Settings/ müssen getrackt sein (außer versteckte/*~).
while IFS= read -r -d '' f; do
    rel="${f#./}"
    if ! git ls-files --error-unmatch "$rel" >/dev/null 2>&1; then
        fail "Pflichtdatei nicht getrackt: $rel"
    fi
done < <(find client/Assets/Settings -type f ! -name ".*" ! -name "*~" -print0 2>/dev/null)

# Alle *.inputactions und *.asmdef unter Assets/ müssen getrackt sein.
while IFS= read -r -d '' f; do
    rel="${f#./}"
    if ! git ls-files --error-unmatch "$rel" >/dev/null 2>&1; then
        fail "Pflichtdatei nicht getrackt: $rel"
    fi
done < <(find client/Assets \( -iname "*.inputactions" -o -iname "*.asmdef" \) -print0 2>/dev/null)

echo "== (ii) Keine unversionierten, nicht ignorierten Dateien =="
UNTRACKED="$(git status --porcelain | grep -E '^\?\?' || true)"
if [ -n "$UNTRACKED" ]; then
    fail "Unversionierte, nicht ignorierte Dateien gefunden:"
    echo "$UNTRACKED" >&2
fi

echo "== (iii) Keine getrackten Dateien, die ignoriert würden =="
TRACKED_BUT_IGNORED="$(git ls-files -ci --exclude-standard)"
if [ -n "$TRACKED_BUT_IGNORED" ]; then
    fail "Getrackte, aber eigentlich ignorierte Dateien gefunden (Gitignore-Regel widerspricht dem Index):"
    echo "$TRACKED_BUT_IGNORED" >&2
fi

echo "== (iv) Jede Datei/jeder Ordner unter client/Assets hat eine passende .meta =="
# Absichtlich ignorierte Inhalte (Plugins/, StreamingAssets/lumiis-data/) brauchen keine
# getrackte .meta - wenn der Inhalt selbst ignoriert ist, ist es seine .meta auch.
cd client || exit 1
while IFS= read -r -d '' entry; do
    if git -C "$REPO_ROOT" check-ignore -q "client/$entry"; then
        continue
    fi

    meta="${entry}.meta"
    if [ ! -e "$meta" ]; then
        fail "Fehlende .meta-Datei: $meta (für $entry)"
    elif ! git -C "$REPO_ROOT" ls-files --error-unmatch "client/$meta" >/dev/null 2>&1; then
        fail "Vorhandene, aber nicht getrackte .meta-Datei: client/$meta"
    fi
done < <(find Assets -mindepth 1 \( -name ".*" -o -name "*~" -o -name "*.meta" \) -prune -o -print0)
cd "$REPO_ROOT" || exit 1

echo
if [ "$FAILED" -eq 0 ]; then
    echo "check-repo.sh: alles sauber."
else
    echo "check-repo.sh: Verstöße gefunden, siehe oben." >&2
fi

exit $FAILED
