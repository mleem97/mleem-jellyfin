#!/usr/bin/env bash
# SQLite integrity repair for Jellyfin music libraries.
#
# Fixes the typical breakage pattern that makes SafeRenameService necessary:
#   - SQLite Error 19: FOREIGN KEY constraint failed (UserData <-> BaseItems)
#   - orphaned UserData rows after manual file moves
#
# Usage (host):   ./docker/fix-sqlite-foreign-keys.sh /path/to/jellyfin/data
# Usage (docker): docker compose run --rm db-doctor "PRAGMA foreign_key_check;"
set -euo pipefail

DB="${1:-/var/lib/jellyfin/data/jellyfin.db}"

if [[ ! -f "$DB" ]]; then
    echo "Database not found: $DB" >&2
    exit 1
fi

echo ">> Stopping hint: make sure Jellyfin is not writing while repairing."
echo ">> Integrity check before:"
sqlite3 "$DB" "PRAGMA integrity_check;"

echo ">> Foreign key violations:"
sqlite3 "$DB" "PRAGMA foreign_key_check;" || true

echo ">> Removing orphaned UserData rows (BaseItems gone)..."
sqlite3 "$DB" "
BEGIN;
DELETE FROM UserData
WHERE ItemId IN (SELECT ItemId FROM UserData EXCEPT SELECT Id FROM BaseItems);
COMMIT;"

echo ">> Rebuilding indexes and vacuuming..."
sqlite3 "$DB" "PRAGMA foreign_keys=ON; VACUUM; ANALYZE;"

echo ">> Integrity check after:"
sqlite3 "$DB" "PRAGMA integrity_check;"
echo ">> Done."
