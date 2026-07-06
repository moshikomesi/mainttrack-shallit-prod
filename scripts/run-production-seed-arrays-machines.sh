#!/usr/bin/env bash
# Production seed: arrays + machines hierarchy for Morning Round v2.
set -euo pipefail

CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
DB="${MAINTTRACK_DB_NAME:-mainttrack_dev}"
TENANT_ID="${MAINTTRACK_TENANT_ID:-00000000-0000-0000-0000-000000000001}"
SCRIPT_DIR="$(dirname "$0")"

echo "[seed] Tenant ID: ${TENANT_ID}"
echo "[seed] Database:  ${DB}"
echo "[seed] Applying production arrays/machines seed..."

docker exec -i "$CONTAINER" psql -U postgres -d "$DB" \
  -v tenant_id="${TENANT_ID}" \
  < "${SCRIPT_DIR}/production-seed-arrays-machines.sql"

echo "[seed] Done."
