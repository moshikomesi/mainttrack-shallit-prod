#!/usr/bin/env bash
# ⚠️  OBSOLETE — DO NOT USE FOR PRODUCTION ⚠️
# Runs the obsolete 4-array mapping (scripts/obsolete/production-seed-shallit-array-mapping.sql).
# Use scripts/run-production-seed-arrays-machines.sh instead. See scripts/README.md.
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
