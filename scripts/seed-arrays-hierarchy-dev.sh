#!/usr/bin/env bash
# Dev/test only: seed Arrays → Machines hierarchy for Morning Round v2.
# Usage: ./scripts/seed-arrays-hierarchy-dev.sh

set -euo pipefail

CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
DB="${MAINTTRACK_PG_DB:-mainttrack_dev}"

docker exec -i "$CONTAINER" psql -U postgres -d "$DB" < "$(dirname "$0")/seed-arrays-hierarchy-dev.sql"
