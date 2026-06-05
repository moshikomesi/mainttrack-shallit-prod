#!/usr/bin/env bash
# Post-export: remove lock file from deliverable repo (production config is applied in export runbook).
set -euo pipefail
DEST="${1:?Usage: finalize-shallit-prod.sh DEST_DIR}"
[[ -d "${DEST}/backend" ]] || { echo "Invalid DEST: ${DEST}" >&2; exit 1; }
rm -f "${DEST}/.export-lock"
echo "[finalize] Removed .export-lock from ${DEST}"
