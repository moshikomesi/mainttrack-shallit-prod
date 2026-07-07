#!/usr/bin/env bash
# Dev/test only: enable the new-feature flags for the local "admin" user
# (Pilot Factory tenant) so it can be used to verify per-user feature flag
# routing (Morning Round V2 / new Maintenance Log) end to end.
# Usage: ./scripts/seed-user-feature-flags-dev.sh

set -euo pipefail

CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
DB="${MAINTTRACK_PG_DB:-mainttrack_dev}"

docker exec -i "$CONTAINER" psql -U postgres -d "$DB" < "$(dirname "$0")/seed-user-feature-flags-dev.sql"
