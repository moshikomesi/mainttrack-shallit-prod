#!/usr/bin/env bash
#
# export-production.sh — Non-destructive production snapshot export for MaintTrack.
# Reads SOURCE_DIR only; writes to DEST_DIR. Never modifies the source repository.
#
# Usage:
#   export-production.sh [OPTIONS] [SOURCE_DIR] [DEST_DIR]
#
# Options:
#   -n, --dry-run       Show what would be copied; do not write files
#   -t, --timestamp     Append YYYYMMDD-HHMM to default destination name
#   -p, --progress      Show rsync progress (rsync >= 3.1)
#   -b, --backup        Backup existing DEST to DEST_backup_YYYYMMDD_HHMM before sync
#   --allow-delete      Allow rsync --delete on DEST only (off by default)
#   -h, --help          Show help

set -euo pipefail

readonly SCRIPT_NAME="$(basename "${BASH_SOURCE[0]}")"
readonly LOCK_FILE_NAME=".export-lock"
readonly DEFAULT_EXPORT_BASE="${HOME}/mainttrack-exports"
readonly DEFAULT_EXPORT_NAME="mainttrack-shallit-prod"

OPTIONAL_DIRS=(nginx deployment scripts infra)
ROOT_DEPLOY_GLOBS=(
  ".github/workflows/deploy-*.yml"
  ".github/workflows/*deploy*.yml"
  "scripts/*.sh"
  "deploy/*.sh"
)

FLAG_TIMESTAMP=0
FLAG_PROGRESS=0
FLAG_DRY_RUN=0
FLAG_BACKUP=0
FLAG_ALLOW_DELETE=0

RSYNC_BACKEND_EXCLUDES=(
  --exclude 'bin/'
  --exclude 'obj/'
  --exclude '.vs/'
  --exclude 'publish/'
  --exclude 'logs/'
  --exclude 'wwwroot/uploads/'
  --exclude 'MaintTrack.Tests/'
)
RSYNC_FRONTEND_EXCLUDES=(
  --exclude 'node_modules/'
  --exclude 'dist/'
  --exclude 'build/'
  --exclude '.env'
  --exclude '.env.local'
)

log() { printf '[export] %s\n' "$*"; }
die() { printf '[export] ERROR: %s\n' "$*" >&2; exit 1; }

usage() {
  sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

resolve_abs_dir() {
  local path="$1"
  [[ -n "${path}" && -d "${path}" ]] || return 1
  (cd "${path}" && pwd)
}

safe_validate_src() {
  local src="$1"
  [[ -n "${src}" ]] || die "SAFE MODE: SOURCE_DIR is empty or undefined."
  [[ -e "${src}" ]] || die "SAFE MODE: SOURCE_DIR does not exist: ${src}"
  local resolved
  resolved="$(resolve_abs_dir "${src}")" || die "SAFE MODE: SOURCE_DIR is not a directory: ${src}"
  [[ "${resolved}" != "/" ]] || die "SAFE MODE: SOURCE_DIR cannot be filesystem root (/)."
  if [[ ! -d "${resolved}/backend" && ! -d "${resolved}/frontend" ]]; then
    die "SAFE MODE: SOURCE_DIR is not a valid MaintTrack root: ${resolved}"
  fi
  printf '%s\n' "${resolved}"
}

safe_validate_dest() {
  local dest="$1" src="$2"
  [[ -n "${dest}" ]] || die "SAFE MODE: DEST_DIR is empty or undefined."
  [[ "${dest}" != "/" ]] || die "SAFE MODE: DEST_DIR cannot be filesystem root (/)."
  if [[ -e "${dest}" && ! -d "${dest}" ]]; then
    die "SAFE MODE: DEST_DIR exists but is not a directory: ${dest}"
  fi
  if [[ "${FLAG_DRY_RUN}" -eq 0 ]]; then
    mkdir -p "${dest}"
  fi
  local resolved
  if [[ "${FLAG_DRY_RUN}" -eq 1 && ! -d "${dest}" ]]; then
    resolved="$(cd "$(dirname "${dest}")" && pwd)/$(basename "${dest}")"
  else
    resolved="$(resolve_abs_dir "${dest}")" || die "SAFE MODE: DEST_DIR is not a directory: ${dest}"
  fi
  [[ "${resolved}" != "/" ]] || die "SAFE MODE: DEST_DIR cannot be filesystem root (/)."
  [[ "${resolved}" != "${src}" ]] || die "SAFE MODE: DEST_DIR cannot equal SOURCE_DIR."
  case "${resolved}" in
    "${src}"/*) die "SAFE MODE: DEST_DIR cannot be inside SOURCE_DIR." ;;
  esac
  case "${src}" in
    "${resolved}"/*) die "SAFE MODE: SOURCE_DIR cannot be inside DEST_DIR." ;;
  esac
  printf '%s\n' "${resolved}"
}

check_dest_lock() {
  local dest="$1"
  [[ ! -f "${dest}/${LOCK_FILE_NAME}" ]] || die "SAFE MODE: lock exists (${dest}/${LOCK_FILE_NAME}). Remove it or choose another DEST."
}

default_dest_path() {
  local name="${DEFAULT_EXPORT_NAME}"
  [[ "${FLAG_TIMESTAMP}" -eq 1 ]] && name="${name}-$(date +%Y%m%d-%H%M)"
  printf '%s/%s\n' "${DEFAULT_EXPORT_BASE}" "${name}"
}

count_files() {
  local root="$1"
  [[ -d "${root}" ]] || { printf '0'; return 0; }
  find "${root}" \
    \( -path '*/node_modules/*' -o -path '*/bin/*' -o -path '*/obj/*' -o \
       -path '*/.vs/*' -o -path '*/publish/*' -o -path '*/build/*' -o \
       -path '*/dist/*' -o -path '*/logs/*' -o -path '*/wwwroot/uploads/*' \) \
    -prune -o -type f -print 2>/dev/null | wc -l | tr -d '[:space:]'
}

require_rsync() {
  command -v rsync >/dev/null 2>&1 || die "rsync is required but not installed."
}

RSYNC_OPTS=()

build_rsync_opts() {
  RSYNC_OPTS=(-aH)
  [[ "${FLAG_PROGRESS}" -eq 1 ]] && RSYNC_OPTS+=(--info=progress2)
  [[ "${FLAG_DRY_RUN}" -eq 1 ]] && RSYNC_OPTS+=(--dry-run)
  if [[ "${FLAG_ALLOW_DELETE}" -eq 1 ]]; then
    RSYNC_OPTS+=(--delete)
    log "WARN: --allow-delete enabled; extra files in DEST may be removed."
  fi
}

sync_dir() {
  local label="$1" from="$2" to="$3"
  shift 3
  [[ -d "${from}" ]] || { log "SKIP ${label}: not found (${from})"; return 0; }
  build_rsync_opts
  [[ "${FLAG_DRY_RUN}" -eq 0 ]] && mkdir -p "${to}"
  log "STEP syncing ${label}: ${from} -> ${to}"
  if [[ $# -gt 0 ]]; then
    rsync "${RSYNC_OPTS[@]}" "$@" "${from}/" "${to}/"
  else
    rsync "${RSYNC_OPTS[@]}" "${from}/" "${to}/"
  fi
  log "STEP ${label}: sync complete"
}

backup_dest_if_needed() {
  local dest="$1"
  [[ "${FLAG_BACKUP}" -eq 1 && "${FLAG_DRY_RUN}" -eq 0 && -d "${dest}" ]] || return 0
  local backup_path="${dest}_backup_$(date +%Y%m%d_%H%M)"
  [[ ! -e "${backup_path}" ]] || die "SAFE MODE: backup path already exists: ${backup_path}"
  log "STEP backup: ${dest} -> ${backup_path}"
  cp -a "${dest}" "${backup_path}"
}

copy_file_if_exists() {
  local src_file="$1" dest_file="$2"
  [[ -f "${src_file}" ]] || return 0
  if [[ "${FLAG_DRY_RUN}" -eq 1 ]]; then
    log "DRY-RUN copy: ${src_file} -> ${dest_file}"
    return 0
  fi
  mkdir -p "$(dirname "${dest_file}")"
  cp -f "${src_file}" "${dest_file}"
  log "Copied $(basename "${src_file}")"
}

copy_glob_files() {
  local label="$1" dest_root="$2"
  shift 2
  local copied=0 pattern match rel target
  for pattern in "$@"; do
    shopt -s nullglob
    local matches=("${SRC}"/${pattern})
    shopt -u nullglob
    [[ ${#matches[@]} -eq 0 ]] && continue
    for match in "${matches[@]}"; do
      [[ -f "${match}" ]] || continue
      rel="${match#"${SRC}/"}"
      target="${dest_root}/${rel}"
      if [[ "${FLAG_DRY_RUN}" -eq 1 ]]; then
        log "DRY-RUN copy: ${match} -> ${target}"
      else
        mkdir -p "$(dirname "${target}")"
        cp -f "${match}" "${target}"
      fi
      copied=$((copied + 1))
    done
  done
  log "STEP ${label}: ${copied} file(s)"
}

post_sync_validate() {
  local dest="$1" failed=0
  log "STEP post-sync validation"
  if [[ ! -d "${dest}/backend" ]] || [[ -z "$(find "${dest}/backend" -name '*.csproj' -print -quit 2>/dev/null)" ]]; then
    log "VALIDATION FAIL: backend .csproj missing"; failed=1
  fi
  if [[ ! -f "${dest}/backend/MaintTrack.Api/Program.cs" ]]; then
    log "VALIDATION FAIL: backend/MaintTrack.Api/Program.cs missing"; failed=1
  fi
  if [[ ! -f "${dest}/frontend/package.json" || ! -d "${dest}/frontend/src" ]]; then
    log "VALIDATION FAIL: frontend package.json or src/ missing"; failed=1
  fi
  [[ "${failed}" -eq 0 ]] || die "Post-sync validation failed."
  log "STEP validation: passed"
}

parse_args() {
  SRC_INPUT="" DEST_INPUT=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -t|--timestamp) FLAG_TIMESTAMP=1; shift ;;
      -p|--progress)  FLAG_PROGRESS=1; shift ;;
      -n|--dry-run)   FLAG_DRY_RUN=1; shift ;;
      -b|--backup)    FLAG_BACKUP=1; shift ;;
      --allow-delete) FLAG_ALLOW_DELETE=1; shift ;;
      -h|--help) usage; exit 0 ;;
      --) shift; [[ $# -gt 0 ]] && SRC_INPUT=$1 && shift; [[ $# -gt 0 ]] && DEST_INPUT=$1 && shift; break ;;
      -*) die "Unknown option: $1" ;;
      *) [[ -z "${SRC_INPUT}" ]] && SRC_INPUT=$1 || DEST_INPUT=$1; shift ;;
    esac
  done
}

main() {
  parse_args "$@"
  require_rsync

  local script_dir default_src
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  default_src="$(cd "${script_dir}/.." && pwd)"

  log "STEP validating paths (SAFE MODE)"
  SRC="$(safe_validate_src "${SRC_INPUT:-${default_src}}")"
  DEST="$(safe_validate_dest "${DEST_INPUT:-$(default_dest_path)}" "${SRC}")"

  log "Source (read-only):  ${SRC}"
  log "Destination (write): ${DEST}"
  [[ "${FLAG_DRY_RUN}" -eq 1 ]] && log "Mode: DRY-RUN"

  check_dest_lock "${DEST}"
  backup_dest_if_needed "${DEST}"
  [[ "${FLAG_DRY_RUN}" -eq 0 ]] && mkdir -p "${DEST}"

  log "STEP syncing backend"
  sync_dir "backend" "${SRC}/backend" "${DEST}/backend" "${RSYNC_BACKEND_EXCLUDES[@]}"

  log "STEP syncing frontend"
  sync_dir "frontend" "${SRC}/frontend" "${DEST}/frontend" "${RSYNC_FRONTEND_EXCLUDES[@]}"

  log "STEP syncing optional directories"
  for dir in "${OPTIONAL_DIRS[@]}"; do
    sync_dir "${dir}" "${SRC}/${dir}" "${DEST}/${dir}"
  done

  log "STEP copying root and config files"
  copy_file_if_exists "${SRC}/README.md" "${DEST}/README.md"
  copy_file_if_exists "${SRC}/.gitignore" "${DEST}/.gitignore"
  shopt -s nullglob
  local sln f
  for sln in "${SRC}"/*.sln; do copy_file_if_exists "${sln}" "${DEST}/$(basename "${sln}")"; done
  for f in "${SRC}/docker-compose.yml" "${SRC}/docker-compose.yaml" "${SRC}"/docker-compose.*.yml "${SRC}"/docker-compose.*.yaml; do
    [[ -f "${f}" ]] && copy_file_if_exists "${f}" "${DEST}/$(basename "${f}")"
  done
  shopt -u nullglob
  copy_glob_files "deployment scripts" "${DEST}" "${ROOT_DEPLOY_GLOBS[@]}"

  if [[ "${FLAG_DRY_RUN}" -eq 0 ]]; then
    post_sync_validate "${DEST}"
    printf 'locked_at=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "${DEST}/${LOCK_FILE_NAME}"
  else
    log "STEP post-sync validation: skipped (dry-run)"
  fi

  echo ""
  log "Export summary"
  log "  Backend files:  $(count_files "${DEST}/backend")"
  log "  Frontend files: $(count_files "${DEST}/frontend")"
  log "  Total files:    $(count_files "${DEST}")"
  log "  Output path:    ${DEST}"
  log "  Status:         SUCCESS"
}

main "$@"
