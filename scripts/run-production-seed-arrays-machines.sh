#!/usr/bin/env bash
# Apply the canonical production arrays + machines hierarchy seed (no EF migrations).
#
# Two connection modes are supported:
#
#   1) Docker-based Postgres (local/TEST environments):
#        Uses MAINTTRACK_PG_CONTAINER / MAINTTRACK_DB_NAME / MAINTTRACK_TENANT_ID
#        (defaults below).
#        Example:
#          ./scripts/run-production-seed-arrays-machines.sh
#
#   2) Direct remote Postgres (real production host):
#        Set MODE=remote and provide PROD_PGHOST / PROD_PGUSER / PROD_DB /
#        PROD_TENANT_ID (defaults to the Shallit production tenant).
#        Example:
#          MODE=remote PROD_PGHOST=<host> PROD_PGUSER=<user> PROD_DB=mainttrack \
#            ./scripts/run-production-seed-arrays-machines.sh
#
set -euo pipefail

SCRIPT_DIR="$(dirname "$0")"
SQL_FILE="${SCRIPT_DIR}/production-seed-arrays-machines.sql"
MODE="${MODE:-docker}"

if [[ "${MODE}" == "remote" ]]; then
  PROD_PGHOST="${PROD_PGHOST:?Set PROD_PGHOST}"
  PROD_PGUSER="${PROD_PGUSER:?Set PROD_PGUSER}"
  PROD_DB="${PROD_DB:-mainttrack}"
  PROD_TENANT_ID="${PROD_TENANT_ID:-beed1fc4-ffbb-4ea1-b7c8-d84584506842}"

  echo "[seed-arrays-machines] Mode:   remote"
  echo "[seed-arrays-machines] Host:   ${PROD_PGHOST}"
  echo "[seed-arrays-machines] DB:     ${PROD_DB}"
  echo "[seed-arrays-machines] Tenant: ${PROD_TENANT_ID}"
  read -r -p "Type 'apply' to confirm running against this database: " CONFIRM
  [[ "${CONFIRM}" == "apply" ]] || { echo "[seed-arrays-machines] Aborted."; exit 1; }

  PGPASSWORD="${PROD_PGPASSWORD:-}" psql \
    "host=${PROD_PGHOST} user=${PROD_PGUSER} dbname=${PROD_DB} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -v tenant_id="${PROD_TENANT_ID}" \
    -f "${SQL_FILE}"
else
  CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
  DB="${MAINTTRACK_DB_NAME:-mainttrack_dev}"
  TENANT_ID="${MAINTTRACK_TENANT_ID:-00000000-0000-0000-0000-000000000001}"

  echo "[seed-arrays-machines] Mode:      docker"
  echo "[seed-arrays-machines] Container: ${CONTAINER}"
  echo "[seed-arrays-machines] Database:  ${DB}"
  echo "[seed-arrays-machines] Tenant:    ${TENANT_ID}"

  docker exec -i "$CONTAINER" psql -U postgres -d "$DB" \
    -v ON_ERROR_STOP=1 \
    -v tenant_id="${TENANT_ID}" \
    < "${SQL_FILE}"
fi

echo "[seed-arrays-machines] Done."
