#!/usr/bin/env bash
# Prüft den Zustand des Repos auf die Fehlerklasse "generische Gitignore-Regel verschluckt
# Unity-Dateien" (siehe CLAUDE.md-Konvention "Ignore-Regeln pro Teilbaum"). Nicht destruktiv,
# meldet nur. Exit-Code 0 = alles sauber, sonst != 0.
#
# POSIX-sh-kompatibel (keine Arrays, kein process substitution), läuft daher gleichermaßen mit
# ./tools/check-repo.sh, bash tools/check-repo.sh oder sh tools/check-repo.sh.
#
# Aufruf: ./tools/check-repo.sh (vor jedem Push, siehe CLAUDE.md; nicht mit sh aufrufen ist NICHT
# nötig - beides funktioniert)

set -u

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
cd "$REPO_ROOT" || exit 1

FAILED=0

fail() {
    echo "FEHLER: $1" >&2
    FAILED=1
}

# Iteriert über die Zeilen von $1 (newline-getrennt), ohne Pipe-Subshell (POSIX-sh startet für
# "cmd | while read" sonst eine Subshell, in der Variablenänderungen wie FAILED=1 verlorengehen)
# und ohne Pfad-Glob-Expansion.
each_line() {
    old_ifs=$IFS
    IFS='
'
    set -f
    for _line in $1; do
        "$2" "$_line"
    done
    set +f
    IFS=$old_ifs
}

echo "== (i) Pflichtdateien getrackt =="
for f in \
    "client/ProjectSettings/ProjectVersion.txt" \
    "client/Packages/manifest.json" \
    "client/Packages/packages-lock.json"
do
    if ! git ls-files --error-unmatch "$f" >/dev/null 2>&1; then
        fail "Pflichtdatei nicht getrackt: $f"
    fi
done

check_tracked() {
    rel=${1#./}
    if ! git ls-files --error-unmatch "$rel" >/dev/null 2>&1; then
        fail "Pflichtdatei nicht getrackt: $rel"
    fi
}

# Alle *.asset unter ProjectSettings/ müssen getrackt sein.
each_line "$(find client/ProjectSettings -maxdepth 1 -name "*.asset" 2>/dev/null)" check_tracked

# Alle Dateien unter Assets/Settings/ müssen getrackt sein (außer versteckte/*~).
each_line "$(find client/Assets/Settings -type f ! -name ".*" ! -name "*~" 2>/dev/null)" check_tracked

# Alle *.inputactions und *.asmdef unter Assets/ müssen getrackt sein.
each_line "$(find client/Assets \( -iname "*.inputactions" -o -iname "*.asmdef" \) 2>/dev/null)" check_tracked

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
check_meta() {
    entry="$1"
    if git -C "$REPO_ROOT" check-ignore -q "client/$entry"; then
        return
    fi

    meta="${entry}.meta"
    if [ ! -e "$meta" ]; then
        fail "Fehlende .meta-Datei: $meta (für $entry)"
    elif ! git -C "$REPO_ROOT" ls-files --error-unmatch "client/$meta" >/dev/null 2>&1; then
        fail "Vorhandene, aber nicht getrackte .meta-Datei: client/$meta"
    fi
}
cd client || exit 1
each_line "$(find Assets -mindepth 1 \( -name ".*" -o -name "*~" -o -name "*.meta" \) -prune -o -print 2>/dev/null)" check_meta
cd "$REPO_ROOT" || exit 1

echo
if [ "$FAILED" -eq 0 ]; then
    echo "check-repo.sh: alles sauber."
else
    echo "check-repo.sh: Verstöße gefunden, siehe oben." >&2
fi

exit $FAILED
