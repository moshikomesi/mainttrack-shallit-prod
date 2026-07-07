#!/usr/bin/env bash
# Create the TEST database (mainttrack_test) on the target Postgres server.
#
# Does NOT touch the Production database. Only creates a new database + role
# if they don't already exist (see create-test-database.sql for details).
#
# Usage:
#   PGHOST=<server-ip-or-host> PGUSER=postgres ./scripts/create-test-database.sh
#
# Review before running. Not executed automatically.

set -euo pipefail

PGHOST="${PGHOST:?Set PGHOST (Postgres server host/IP)}"
PGUSER="${PGUSER:-postgres}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "[create-test-db] Target host: ${PGHOST}"
echo "[create-test-db] This will ONLY create/verify 'mainttrack_test' — Production DB is not touched."
read -r -p "Continue? [y/N] " confirm
if [[ "${confirm}" != "y" && "${confirm}" != "Y" ]]; then
  echo "Aborted."
  exit 1
fi

psql -h "${PGHOST}" -U "${PGUSER}" -d postgres -f "${SCRIPT_DIR}/create-test-database.sql"

echo "[create-test-db] Done. Next: run EF migrations against mainttrack_test (see TEST_ENVIRONMENT_SETUP.md)."
