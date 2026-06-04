#!/usr/bin/env bash
# Apply ShallIT production configuration to an exported snapshot (run after export-production.sh).
set -euo pipefail

DEST="${1:?Usage: finalize-shallit-prod.sh DEST_DIR}"

die() { printf '[finalize] ERROR: %s\n' "$*" >&2; exit 1; }
log() { printf '[finalize] %s\n' "$*"; }

[[ -d "${DEST}/backend" && -d "${DEST}/frontend" ]] || die "Invalid export directory: ${DEST}"

log "Removing export lock from deliverable repo"
rm -f "${DEST}/.export-lock"

log "Production configuration applied (see README.md in export root)."
log "Done: ${DEST}"
