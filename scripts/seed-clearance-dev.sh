#!/usr/bin/env bash
# Dev/test only: add local Clearance test users + approver assignments
# (see seed-clearance-dev.sql). Additive and idempotent; never touches
# existing users. Targets the local Docker database only.
#
# Usage:
#   MAINTTRACK_DEV_CLEARANCE_PASSWORD='<choose a local password>' ./scripts/seed-clearance-dev.sh
#
# The password is bcrypt-hashed locally (htpasswd) and never written to disk.

set -euo pipefail

CONTAINER="${MAINTTRACK_PG_CONTAINER:-mainttrack-postgres}"
DB="${MAINTTRACK_PG_DB:-mainttrack_dev}"
PASSWORD="${MAINTTRACK_DEV_CLEARANCE_PASSWORD:?Set MAINTTRACK_DEV_CLEARANCE_PASSWORD (local test-user password)}"

if [[ "$DB" != *_dev ]]; then
  echo "Refusing to seed '$DB': only *_dev databases are allowed." >&2
  exit 1
fi

HASH="$(htpasswd -nbBC 10 "" "$PASSWORD" | tr -d ':\n')"

docker exec -i "$CONTAINER" psql -U postgres -d "$DB" -v password_hash="$HASH" \
  < "$(dirname "$0")/seed-clearance-dev.sql"
