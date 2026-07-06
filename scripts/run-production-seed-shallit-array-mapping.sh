#!/usr/bin/env bash
# Production seed: map existing Shallit machines into Morning Round v2 arrays.
set -euo pipefail

CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
DB="${MAINTTRACK_DB_NAME:-mainttrack_dev}"
SCRIPT_DIR="$(dirname "$0")"

echo "[seed] Tenant: beed1fc4-ffbb-4ea1-b7c8-d84584506842 (fixed in SQL)"
echo "[seed] Database: ${DB}"
echo "[seed] Applying Shallit array mapping..."

docker exec -i "$CONTAINER" psql -U postgres -d "$DB" \
  < "${SCRIPT_DIR}/production-seed-shallit-array-mapping.sql"

echo "[seed] Done."
