#!/usr/bin/env bash
# =============================================================================
# deploy/deploy.sh — Déploiement GISEBS Pay Gateway sur serveur Linux (systemd)
#
# Configuration : deploy/project.config.json
#
# Modes :
#   1) Sur le serveur après git pull : ./deploy/deploy.sh
#   2) Avec publish précompilé :       ./deploy/deploy.sh /chemin/vers/publish
#
# Mise à jour BD (avant ou après déploiement) :
#   dotnet ef database update
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_CONFIG="${SCRIPT_DIR}/project.config.json"

log()  { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }
die()  { log "ERREUR: $*" >&2; exit 1; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "Commande introuvable : $1"
}

load_project_config() {
  [[ -f "${PROJECT_CONFIG}" ]] || die "Fichier manquant : ${PROJECT_CONFIG}. Copiez deploy/project.config.example.json vers deploy/project.config.json."

  require_cmd python3

  eval "$(python3 - "${PROJECT_CONFIG}" <<'PY'
import json, shlex, sys

path = sys.argv[1]
with open(path, encoding="utf-8") as f:
    cfg = json.load(f)

required = ("appName", "serviceName", "appRoot", "dllName", "projectPath", "healthCheckUrl", "listenPort")
missing = [k for k in required if not cfg.get(k)]
if missing:
    raise SystemExit(f"Clés manquantes dans {path}: {', '.join(missing)}")

exports = {
    "APP_NAME": cfg["appName"],
    "SERVICE_NAME": cfg["serviceName"],
    "APP_ROOT": cfg["appRoot"],
    "DLL_NAME": cfg["dllName"],
    "PROJECT_REL_PATH": cfg["projectPath"],
    "HEALTHCHECK_URL": cfg["healthCheckUrl"],
    "LISTEN_PORT": str(cfg["listenPort"]),
}

for key, value in exports.items():
    print(f"export {key}={shlex.quote(str(value))}")
PY
)"
}

load_project_config

APP_DIR="${APP_ROOT}/app"
BACKUP_DIR="${APP_ROOT}/backups"
STAGING_DIR="${APP_ROOT}/staging"
DOTNET="${DOTNET:-/usr/bin/dotnet}"
SKIP_PUBLISH="${SKIP_PUBLISH:-0}"
SKIP_SYSTEMD="${SKIP_SYSTEMD:-0}"
SKIP_HEALTHCHECK="${SKIP_HEALTHCHECK:-0}"
SKIP_EF_MIGRATE="${SKIP_EF_MIGRATE:-0}"

PROJECT_CSPROJ="${REPO_ROOT}/${PROJECT_REL_PATH}"

PUBLISH_SOURCE=""
if [[ $# -gt 0 ]]; then
  if [[ "$1" == "--help" || "$1" == "-h" ]]; then
    echo "Usage: $0 [chemin/publish]"
    echo "Config : deploy/project.config.json"
    echo "Variables : SKIP_PUBLISH, SKIP_SYSTEMD, SKIP_HEALTHCHECK, SKIP_EF_MIGRATE"
    exit 0
  fi
  PUBLISH_SOURCE="$1"
  [[ -d "${PUBLISH_SOURCE}" ]] || die "Répertoire publish introuvable : ${PUBLISH_SOURCE}"
fi

require_cmd rsync
require_cmd curl
[[ -x "${DOTNET}" || -n "${PUBLISH_SOURCE}" ]] || die "${DOTNET} introuvable."

log "Projet : ${APP_NAME} (service ${SERVICE_NAME})"

if [[ -z "${PUBLISH_SOURCE}" ]]; then
  [[ -f "${PROJECT_CSPROJ}" ]] || die "Projet introuvable : ${PROJECT_CSPROJ}"
  if [[ "${SKIP_PUBLISH}" != "1" ]]; then
    log "Publication Release depuis ${PROJECT_CSPROJ}..."
    mkdir -p "${STAGING_DIR}"
    PUBLISH_DIR="${STAGING_DIR}/publish-$$"
    rm -rf "${PUBLISH_DIR}"
    "${DOTNET}" publish "${PROJECT_CSPROJ}" \
      -c Release \
      -o "${PUBLISH_DIR}" \
      --no-self-contained
    PUBLISH_SOURCE="${PUBLISH_DIR}"
    log "Publication terminée : ${PUBLISH_SOURCE}"
  else
    die "SKIP_PUBLISH=1 sans chemin publish fourni."
  fi
fi

[[ -f "${PUBLISH_SOURCE}/${DLL_NAME}" ]] || die "Assembly introuvable : ${PUBLISH_SOURCE}/${DLL_NAME}"

log "Création des répertoires sous ${APP_ROOT}..."
sudo mkdir -p "${APP_DIR}" "${BACKUP_DIR}" "${STAGING_DIR}"

TIMESTAMP="$(date '+%Y%m%d-%H%M%S')"
if [[ -d "${APP_DIR}" ]] && [[ -n "$(ls -A "${APP_DIR}" 2>/dev/null || true)" ]]; then
  BACKUP_PATH="${BACKUP_DIR}/${TIMESTAMP}"
  log "Sauvegarde vers ${BACKUP_PATH}..."
  sudo cp -a "${APP_DIR}" "${BACKUP_PATH}"
fi

PROD_SETTINGS="${APP_DIR}/appsettings.Production.json"
SAVED_SETTINGS=""
if [[ -f "${PROD_SETTINGS}" ]]; then
  SAVED_SETTINGS="$(mktemp)"
  sudo cp "${PROD_SETTINGS}" "${SAVED_SETTINGS}"
  log "appsettings.Production.json préservé."
fi

if systemctl is-active --quiet "${SERVICE_NAME}" 2>/dev/null; then
  log "Arrêt de ${SERVICE_NAME}..."
  sudo systemctl stop "${SERVICE_NAME}" || true
  sleep 1
fi

log "Copie des fichiers publiés vers ${APP_DIR}..."
sudo rsync -a --delete \
  --exclude 'appsettings.Production.json' \
  --exclude 'app.env' \
  --exclude 'keys/' \
  "${PUBLISH_SOURCE}/" "${APP_DIR}/"

if [[ -n "${SAVED_SETTINGS}" ]]; then
  sudo cp "${SAVED_SETTINGS}" "${PROD_SETTINGS}"
  rm -f "${SAVED_SETTINGS}"
fi

if id ubuntu &>/dev/null; then
  sudo chown -R ubuntu:ubuntu "${APP_DIR}"
  sudo mkdir -p "${APP_DIR}/keys" "${APP_DIR}/logs"
  sudo chown -R ubuntu:ubuntu "${APP_DIR}/keys" "${APP_DIR}/logs"
fi

if [[ "${PUBLISH_SOURCE}" == "${STAGING_DIR}/publish-"* ]]; then
  rm -rf "${PUBLISH_SOURCE}"
fi

if [[ "${SKIP_SYSTEMD}" != "1" ]]; then
  APP_SETTINGS="${REPO_ROOT}/$(dirname "${PROJECT_REL_PATH}")/appsettings.json"
  [[ -f "${APP_SETTINGS}" ]] || die "appsettings.json introuvable : ${APP_SETTINGS}"

  DB_CONN="$(python3 - "${APP_SETTINGS}" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    cfg = json.load(f)
conn = cfg.get("ConnectionStrings", {}).get("DefaultConnection", "").strip()
if not conn:
    raise SystemExit("ConnectionStrings:DefaultConnection manquant dans appsettings.json")
print(conn)
PY
)"

  log "Configuration systemd ${SERVICE_NAME}..."
  {
    printf 'UBUNTU1_CONNECTION_STRING=%s\n' "${DB_CONN}"
    printf 'UBUNTU1_APP_ROOT=%s\n' "${APP_ROOT}"
    printf 'UBUNTU1_SERVICE_NAME=%s\n' "${SERVICE_NAME}"
    printf 'UBUNTU1_LISTEN_PORT=%s\n' "${LISTEN_PORT}"
    printf 'UBUNTU1_APP_NAME=%s\n' "${APP_NAME}"
  } | sudo tee "${APP_DIR}/app.env" >/dev/null
  sudo chmod 600 "${APP_DIR}/app.env"
  sudo chown ubuntu:ubuntu "${APP_DIR}/app.env" 2>/dev/null || true

  cat <<EOF | sudo tee "/etc/systemd/system/${SERVICE_NAME}.service" >/dev/null
[Unit]
Description=${APP_NAME}
After=network.target

[Service]
Type=simple
User=ubuntu
Group=ubuntu
WorkingDirectory=${APP_DIR}
ExecStart=/usr/bin/dotnet ${APP_DIR}/${DLL_NAME}
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=-${APP_DIR}/app.env
Restart=always
RestartSec=5
TimeoutStartSec=120
TimeoutStopSec=30
KillSignal=SIGINT
SyslogIdentifier=${SERVICE_NAME}
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

  sudo systemctl daemon-reload
  sudo systemctl enable "${SERVICE_NAME}"
  sudo systemctl restart "${SERVICE_NAME}"
  sudo systemctl status "${SERVICE_NAME}" --no-pager || true
fi

if [[ "${SKIP_EF_MIGRATE}" != "1" ]] && [[ -f "${APP_DIR}/${DLL_NAME}" ]]; then
  log "Application des migrations EF Core..."
  if [[ -f "${APP_DIR}/app.env" ]]; then
    set -a
    # shellcheck disable=SC1091
    source "${APP_DIR}/app.env"
    set +a
  fi
  ASPNETCORE_ENVIRONMENT=Production \
    "${DOTNET}" ef database update \
      --project "${PROJECT_CSPROJ}" \
      --startup-assembly "${APP_DIR}/${DLL_NAME}" \
      --no-build 2>/dev/null || \
  log "Note : exécutez 'dotnet ef database update' manuellement si l'outil EF n'est pas installé sur le serveur."
fi

if [[ "${SKIP_HEALTHCHECK}" != "1" ]]; then
  log "Healthcheck ${HEALTHCHECK_URL}..."
  ok=0
  for i in $(seq 1 45); do
    if curl -fsS -o /dev/null "${HEALTHCHECK_URL}" 2>/dev/null; then
      log "Healthcheck réussi après ${i} tentative(s)."
      ok=1
      break
    fi
    sleep 2
  done
  [[ "${ok}" == "1" ]] || die "Healthcheck échoué sur ${HEALTHCHECK_URL}"
fi

log "Déploiement terminé avec succès."
