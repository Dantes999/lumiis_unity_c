#!/usr/bin/env bash
# Lädt die Repo-Root-.env in die AKTUELLE Shell (set -a exportiert alle per source gesetzten
# Variablen automatisch, set +a schaltet das danach wieder ab). Davor nötig für: docker compose,
# dotnet ef (server/src/Lumiis.Data), dotnet run --project server/src/Lumiis.Auth.
#
# WICHTIG: Mit "source tools/dev-env.sh" einbinden, NICHT mit ./tools/dev-env.sh ausführen - beim
# Ausführen gelten die Variablen nur im Subprozess des Skripts und sind in der eigenen Shell danach
# wieder weg. Siehe CLAUDE.md.

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    echo "FEHLER: dev-env.sh wurde ausgeführt statt eingebunden. Bitte 'source tools/dev-env.sh' verwenden (nicht ./tools/dev-env.sh)." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"

if [ ! -f "$ENV_FILE" ]; then
    echo "FEHLER: $ENV_FILE nicht gefunden. Erst 'cp .env.example .env' ausführen und eigene Werte eintragen." >&2
    return 1
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

echo "Umgebungsvariablen aus .env geladen (LUMIIS_DB_*, LUMIIS_TOKEN_SECRET, ...)."
