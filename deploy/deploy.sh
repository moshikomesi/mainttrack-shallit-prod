#!/usr/bin/env bash
# Deploy built artifacts to a server (requires SSH access and built publish/ + frontend/build).
# Based on .github/workflows/deploy-production.yml — customize variables below.
set -euo pipefail

: "${DEPLOY_HOST:?Set DEPLOY_HOST}"
: "${DEPLOY_USER:?Set DEPLOY_USER}"
: "${DEPLOY_KEY:?Path to SSH private key}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE_API_PATH="${REMOTE_API_PATH:-/var/www/mainttrack/api}"
REMOTE_WEB_PATH="${REMOTE_WEB_PATH:-/var/www/mainttrack/web}"
API_SERVICE="${API_SERVICE:-mainttrack-api}"

SSH_OPTS=(-o StrictHostKeyChecking=no -i "${DEPLOY_KEY}")

[[ -d "${ROOT}/publish/api" ]] || { echo "Run deploy/build.sh first." >&2; exit 1; }
[[ -d "${ROOT}/frontend/build" ]] || { echo "Run deploy/build.sh first." >&2; exit 1; }

echo "[deploy] Preparing remote directories"
ssh "${SSH_OPTS[@]}" "${DEPLOY_USER}@${DEPLOY_HOST}" \
  "mkdir -p ${REMOTE_API_PATH} ${REMOTE_WEB_PATH}"

echo "[deploy] Uploading API"
scp "${SSH_OPTS[@]}" -r "${ROOT}/publish/api/"* \
  "${DEPLOY_USER}@${DEPLOY_HOST}:${REMOTE_API_PATH}/"

echo "[deploy] Uploading web"
scp "${SSH_OPTS[@]}" -r "${ROOT}/frontend/build/"* \
  "${DEPLOY_USER}@${DEPLOY_HOST}:${REMOTE_WEB_PATH}/"

echo "[deploy] Restarting API service"
ssh "${SSH_OPTS[@]}" "${DEPLOY_USER}@${DEPLOY_HOST}" \
  "sudo systemctl restart ${API_SERVICE}"

echo "[deploy] Done."
