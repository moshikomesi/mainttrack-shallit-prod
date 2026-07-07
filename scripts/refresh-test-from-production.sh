#!/usr/bin/env bash
# OPTIONAL: Refresh the TEST database with a copy of Production data.
#
# Direction is ALWAYS one-way: Production -> TEST.
#   1. pg_dump Production (READ-ONLY against Production — no writes, no locks
#      beyond a standard consistent dump).
#   2. Drop + recreate ONLY the `mainttrack_test` database.
#   3. Restore the dump into `mainttrack_test`.
#
# Hard safety rails:
#   - The target database name is hardcoded to `mainttrack_test` and is
#     validated before any destructive step runs.
#   - The script refuses to run if TARGET_DB is anything other than
#     `mainttrack_test`, to prevent an accidental swap of source/target.
#   - Production is only ever read from (pg_dump), never written to.
#
# Usage:
#   PROD_PGHOST=<prod-host> PROD_PGUSER=<readonly-or-admin-user> \
#   TEST_PGHOST=<test-host> TEST_PGUSER=postgres \
#   ./scripts/refresh-test-from-production.sh
#
# Review before running. Not executed automatically.

set -euo pipefail

PROD_PGHOST="${PROD_PGHOST:?Set PROD_PGHOST}"
PROD_PGUSER="${PROD_PGUSER:?Set PROD_PGUSER}"
PROD_DB="${PROD_DB:-mainttrack}"

TEST_PGHOST="${TEST_PGHOST:?Set TEST_PGHOST}"
TEST_PGUSER="${TEST_PGUSER:-postgres}"
TARGET_DB="${TARGET_DB:-mainttrack_test}"

DUMP_FILE="$(mktemp -t mainttrack_prod_dump_XXXXXX.sql)"

# Hard guard: never allow this script to target anything but the test DB.
if [[ "${TARGET_DB}" != "mainttrack_test" ]]; then
  echo "❌ Refusing to run: TARGET_DB must be 'mainttrack_test' (got '${TARGET_DB}')." >&2
  exit 1
fi

# Hard guard: never allow source and target to be the same database/host combo.
if [[ "${PROD_PGHOST}" == "${TEST_PGHOST}" && "${PROD_DB}" == "${TARGET_DB}" ]]; then
  echo "❌ Refusing to run: source and target resolve to the same database." >&2
  exit 1
fi

echo "=============================================================="
echo " Refresh TEST from Production"
echo "=============================================================="
echo " Source (read-only): ${PROD_PGUSER}@${PROD_PGHOST}/${PROD_DB}"
echo " Target (overwritten): ${TEST_PGUSER}@${TEST_PGHOST}/${TARGET_DB}"
echo "=============================================================="
echo " This will PERMANENTLY REPLACE all data in '${TARGET_DB}'."
echo " Production ('${PROD_DB}') will only be read, never modified."
echo "=============================================================="
read -r -p "Type 'refresh-test' to continue: " confirm
if [[ "${confirm}" != "refresh-test" ]]; then
  echo "Aborted."
  exit 1
fi

echo "[1/4] Dumping Production (${PROD_DB}) — read-only..."
pg_dump -h "${PROD_PGHOST}" -U "${PROD_PGUSER}" -d "${PROD_DB}" \
  --no-owner --no-privileges -F c -f "${DUMP_FILE}"

echo "[2/4] Dropping existing TEST database (${TARGET_DB})..."
psql -h "${TEST_PGHOST}" -U "${TEST_PGUSER}" -d postgres \
  -c "DROP DATABASE IF EXISTS ${TARGET_DB};"

echo "[3/4] Recreating TEST database (${TARGET_DB})..."
psql -h "${TEST_PGHOST}" -U "${TEST_PGUSER}" -d postgres \
  -c "CREATE DATABASE ${TARGET_DB} OWNER mainttrack_test_user;"

echo "[4/4] Restoring dump into TEST database (${TARGET_DB})..."
pg_restore -h "${TEST_PGHOST}" -U "${TEST_PGUSER}" -d "${TARGET_DB}" \
  --no-owner --no-privileges "${DUMP_FILE}"

rm -f "${DUMP_FILE}"

echo "✅ TEST database refreshed from Production. Production was not modified."
