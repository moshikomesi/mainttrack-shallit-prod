#!/usr/bin/env bash
# Apply production manual schema for Morning Round v2 + Arrays (no EF migrations).
set -euo pipefail

CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
DB="${MAINTTRACK_DB_NAME:-mainttrack_dev}"
SCRIPT_DIR="$(dirname "$0")"

echo "[schema] Applying production manual schema to database: ${DB}"
docker exec -i "$CONTAINER" psql -U postgres -d "$DB" < "${SCRIPT_DIR}/production-manual-schema-morning-round-v2.sql"
echo "[schema] Done."
