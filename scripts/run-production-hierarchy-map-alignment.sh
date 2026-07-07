#!/usr/bin/env bash
# Apply production hierarchy map alignment (no EF migrations).
#
# Two connection modes are supported:
#
#   1) Docker-based Postgres (local/TEST environments):
#        Uses MAINTTRACK_PG_CONTAINER / MAINTTRACK_DB_NAME (defaults below).
#        Example:
#          ./scripts/run-production-hierarchy-map-alignment.sh
#
#   2) Direct remote Postgres (real production host):
#        Set MODE=remote and provide PROD_PGHOST / PROD_PGUSER / PROD_DB.
#        Example:
#          MODE=remote PROD_PGHOST=<host> PROD_PGUSER=<user> PROD_DB=mainttrack \
#            ./scripts/run-production-hierarchy-map-alignment.sh
#
set -euo pipefail

SCRIPT_DIR="$(dirname "$0")"
SQL_FILE="${SCRIPT_DIR}/production-hierarchy-map-alignment.sql"
MODE="${MODE:-docker}"

if [[ "${MODE}" == "remote" ]]; then
  PROD_PGHOST="${PROD_PGHOST:?Set PROD_PGHOST}"
  PROD_PGUSER="${PROD_PGUSER:?Set PROD_PGUSER}"
  PROD_DB="${PROD_DB:-mainttrack}"

  echo "[hierarchy-alignment] Mode: remote"
  echo "[hierarchy-alignment] Host: ${PROD_PGHOST}"
  echo "[hierarchy-alignment] DB:   ${PROD_DB}"
  read -r -p "Type 'apply' to confirm running against this database: " CONFIRM
  [[ "${CONFIRM}" == "apply" ]] || { echo "[hierarchy-alignment] Aborted."; exit 1; }

  PGPASSWORD="${PROD_PGPASSWORD:-}" psql \
    "host=${PROD_PGHOST} user=${PROD_PGUSER} dbname=${PROD_DB} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -f "${SQL_FILE}"
else
  CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
  DB="${MAINTTRACK_DB_NAME:-mainttrack_dev}"

  echo "[hierarchy-alignment] Mode:      docker"
  echo "[hierarchy-alignment] Container: ${CONTAINER}"
  echo "[hierarchy-alignment] Database:  ${DB}"

  docker exec -i "$CONTAINER" psql -U postgres -d "$DB" -v ON_ERROR_STOP=1 < "${SQL_FILE}"
fi

echo "[hierarchy-alignment] Done."
