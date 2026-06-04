#!/usr/bin/env bash
# Build production artifacts (API publish + frontend static files).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

export VITE_API_URL="${VITE_API_URL:-https://mt.shallit.co.il/api}"

echo "[build] Frontend (VITE_API_URL=${VITE_API_URL})"
cd frontend
npm ci
npm run build

echo "[build] API (Release)"
cd "${ROOT}"
dotnet publish backend/MaintTrack.Api/MaintTrack.Api.csproj -c Release -o "${ROOT}/publish/api"

echo "[build] Done."
echo "  Web: ${ROOT}/frontend/build"
echo "  API: ${ROOT}/publish/api"
