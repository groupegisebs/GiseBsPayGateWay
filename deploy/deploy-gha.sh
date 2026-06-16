#!/usr/bin/env bash
# Déploiement GISEBS Pay Gateway depuis GitHub Actions vers Ubuntu (systemd).
# Variables : voir deploy/README.md § GitHub Actions

set -euo pipefail

: "${SSH_HOST:?SSH_HOST requis}"
: "${SSH_USER:?SSH_USER requis}"
: "${APP_ROOT:?APP_ROOT requis}"
: "${SERVICE_NAME:?SERVICE_NAME requis}"
: "${DLL_NAME:=GiseBsPayGateway.dll}"
: "${LISTEN_PORT:=7843}"
: "${CONNECTION_STRING:?CONNECTION_STRING requis}"
: "${PUBLISH_DIR:=publish}"
: "${SSH_PORT:=22}"
: "${APP_NAME:=GISEBS Pay Gateway}"

sanitize() {
  printf '%s' "$1" | tr -d '\r\n\t' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^"//' -e 's/"$//' -e "s/^'//" -e "s/'$//"
}

SSH_HOST=$(sanitize "${SSH_HOST}")
SSH_HOST="${SSH_HOST#http://}"
SSH_HOST="${SSH_HOST#https://}"
SSH_HOST="${SSH_HOST%%/*}"
SSH_USER=$(sanitize "${SSH_USER}")
SSH_PORT=$(sanitize "${SSH_PORT}")
APP_ROOT=$(sanitize "${APP_ROOT}")
SERVICE_NAME=$(sanitize "${SERVICE_NAME}")
LISTEN_PORT=$(sanitize "${LISTEN_PORT}")
CONNECTION_STRING=$(sanitize "${CONNECTION_STRING}")

if [[ ! "$SSH_HOST" =~ ^[0-9a-zA-Z.-]+$ ]]; then
  echo "SSH_HOST invalide" >&2
  exit 1
fi

APP_DIR="${APP_ROOT}/app"
BACKUP_DIR="${APP_ROOT}/backups"
STAGING_REMOTE="/tmp/${SERVICE_NAME}-gha-$(date +%Y%m%d-%H%M%S)"
SSH_OPTS=(-p "${SSH_PORT}" -o BatchMode=yes -o StrictHostKeyChecking=yes)
SCP_OPTS=(-P "${SSH_PORT}" -o BatchMode=yes -o StrictHostKeyChecking=yes)
SSH_TARGET="${SSH_USER}@${SSH_HOST}"

if [[ -n "${SSH_KEY_PATH:-}" ]]; then
  SSH_OPTS+=(-i "${SSH_KEY_PATH}")
  SCP_OPTS+=(-i "${SSH_KEY_PATH}")
fi

[[ -f "${PUBLISH_DIR}/${DLL_NAME}" ]] || { echo "Assembly introuvable : ${PUBLISH_DIR}/${DLL_NAME}" >&2; exit 1; }

ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" "sudo mkdir -p '${APP_DIR}' '${BACKUP_DIR}' '${APP_DIR}/logs' '${APP_ROOT}' && sudo chown -R ${SSH_USER}:${SSH_USER} '${APP_ROOT}'"

ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" bash -s <<REMOTE_BACKUP
set -eu
TIMESTAMP=\$(date +%Y%m%d-%H%M%S)
if [[ -d '${APP_DIR}' ]] && [[ -n "\$(ls -A '${APP_DIR}' 2>/dev/null || true)" ]]; then
  sudo cp -a '${APP_DIR}' '${BACKUP_DIR}/'\${TIMESTAMP}
  echo "Sauvegarde : ${BACKUP_DIR}/\${TIMESTAMP}"
fi
REMOTE_BACKUP

ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" "mkdir -p '${STAGING_REMOTE}'"
scp "${SCP_OPTS[@]}" -r "${PUBLISH_DIR}/." "${SSH_TARGET}:${STAGING_REMOTE}/"

ENV_FILE="$(mktemp)"
{
  printf 'UBUNTU1_CONNECTION_STRING=%s\n' "${CONNECTION_STRING}"
  printf 'UBUNTU1_APP_ROOT=%s\n' "${APP_ROOT}"
  printf 'UBUNTU1_SERVICE_NAME=%s\n' "${SERVICE_NAME}"
  printf 'UBUNTU1_LISTEN_PORT=%s\n' "${LISTEN_PORT}"
  printf 'UBUNTU1_APP_NAME=%s\n' "${APP_NAME}"
  printf 'GISEBSPAY_SECRETS_FILE=%s/secrets.json\n' "${APP_ROOT}"
} > "${ENV_FILE}"
scp "${SCP_OPTS[@]}" "${ENV_FILE}" "${SSH_TARGET}:/tmp/${SERVICE_NAME}.app.env"
rm -f "${ENV_FILE}"

if [[ -n "${STRIPE_SECRET_KEY:-}" ]]; then
  SECRETS_JSON="$(mktemp)"
  export STRIPE_PUBLISHABLE_KEY="${STRIPE_PUBLISHABLE_KEY:-}"
  export STRIPE_SECRET_KEY
  export STRIPE_WEBHOOK_SECRET="${STRIPE_WEBHOOK_SECRET:-}"
  export STRIPE_LIVE_MODE="${STRIPE_LIVE_MODE:-false}"
  export JWT_SECRET_KEY="${JWT_SECRET_KEY:-}"
  python3 <<'PY' > "${SECRETS_JSON}"
import json, os

live = os.environ.get("STRIPE_LIVE_MODE", "false").strip().lower() in ("1", "true", "yes")
print(json.dumps({
    "Stripe": {
        "PublishableKey": os.environ.get("STRIPE_PUBLISHABLE_KEY", ""),
        "SecretKey": os.environ["STRIPE_SECRET_KEY"],
        "WebhookSecret": os.environ.get("STRIPE_WEBHOOK_SECRET", ""),
        "IsLiveMode": live,
    },
    "Jwt": {
        "SecretKey": os.environ.get("JWT_SECRET_KEY", ""),
    },
}, indent=2))
PY
  scp "${SCP_OPTS[@]}" "${SECRETS_JSON}" "${SSH_TARGET}:/tmp/${SERVICE_NAME}.secrets.json"
  rm -f "${SECRETS_JSON}"
  ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" bash -s <<REMOTE_SECRETS
set -eu
sudo mv "/tmp/${SERVICE_NAME}.secrets.json" "${APP_ROOT}/secrets.json"
sudo chmod 600 "${APP_ROOT}/secrets.json"
sudo chown ${SSH_USER}:${SSH_USER} "${APP_ROOT}/secrets.json"
echo "secrets.json déployé dans ${APP_ROOT}/secrets.json"
REMOTE_SECRETS
else
  echo "Secrets Stripe/JWT non fournis — secrets.json inchangé sur le serveur."
fi

ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" bash -s <<REMOTE_DEPLOY
set -eu
PROD_SETTINGS='${APP_DIR}/appsettings.Production.json'
SAVED_SETTINGS=''
if [[ -f "\${PROD_SETTINGS}" ]]; then
  SAVED_SETTINGS=\$(mktemp)
  cp "\${PROD_SETTINGS}" "\${SAVED_SETTINGS}"
fi

sudo systemctl stop '${SERVICE_NAME}' || true
sleep 1

sudo rsync -a --delete \
  --exclude 'appsettings.Production.json' \
  --exclude 'app.env' \
  '${STAGING_REMOTE}/' '${APP_DIR}/'

if [[ -n "\${SAVED_SETTINGS}" ]] && [[ -f "\${SAVED_SETTINGS}" ]]; then
  sudo cp "\${SAVED_SETTINGS}" "\${PROD_SETTINGS}"
  rm -f "\${SAVED_SETTINGS}"
fi
sudo mv "/tmp/${SERVICE_NAME}.app.env" "${APP_DIR}/app.env"
sudo chmod 600 "${APP_DIR}/app.env"
sudo chown ${SSH_USER}:${SSH_USER} "${APP_DIR}/app.env"
sudo chown -R ${SSH_USER}:${SSH_USER} '${APP_DIR}'
rm -rf '${STAGING_REMOTE}'
REMOTE_DEPLOY

SERVICE_FILE="/tmp/${SERVICE_NAME}.service"
cat > "${SERVICE_FILE}" <<EOF
[Unit]
Description=${APP_NAME}
After=network.target

[Service]
Type=simple
User=${SSH_USER}
Group=${SSH_USER}
WorkingDirectory=${APP_DIR}
ExecStart=/usr/bin/dotnet ${APP_DIR}/${DLL_NAME}
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=-${APP_DIR}/app.env
Restart=always
RestartSec=5
TimeoutStartSec=120
KillSignal=SIGINT
TimeoutStopSec=30
SyslogIdentifier=${SERVICE_NAME}
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

scp "${SCP_OPTS[@]}" "${SERVICE_FILE}" "${SSH_TARGET}:/tmp/${SERVICE_NAME}.service"
rm -f "${SERVICE_FILE}"

ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" "sudo cp '/tmp/${SERVICE_NAME}.service' '/etc/systemd/system/${SERVICE_NAME}.service' && rm -f '/tmp/${SERVICE_NAME}.service'"

DB_NAME="$(printf '%s' "${CONNECTION_STRING}" | sed -n 's/.*[Dd]atabase=\([^;]*\).*/\1/p')"
DB_NAME="${DB_NAME:-gisebs_pay_gateway}"
DB_OWNER="$(printf '%s' "${CONNECTION_STRING}" | sed -n 's/.*[Uu]sername=\([^;]*\).*/\1/p')"
DB_OWNER="${DB_OWNER:-gisedocuser}"

echo "Vérification base PostgreSQL '${DB_NAME}'..."
ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" bash -s <<REMOTE_DB
set -eu
DB_NAME='${DB_NAME}'
DB_OWNER='${DB_OWNER}'
if ! command -v psql >/dev/null 2>&1; then
  echo "::error::psql introuvable sur le serveur — installez postgresql-client"
  exit 1
fi
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='\${DB_NAME}'" | grep -q 1; then
  echo "Création de la base \${DB_NAME} (owner \${DB_OWNER})..."
  sudo -u postgres psql -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"\${DB_NAME}\" OWNER \${DB_OWNER};"
else
  echo "Base \${DB_NAME} déjà existante."
fi
sudo -u postgres psql -d "\${DB_NAME}" -v ON_ERROR_STOP=1 -c "GRANT ALL ON SCHEMA public TO \${DB_OWNER};"
echo "Base \${DB_NAME} prête."
REMOTE_DB

ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" "sudo systemctl daemon-reload && sudo systemctl enable ${SERVICE_NAME} && sudo systemctl start ${SERVICE_NAME}"

echo "Attente démarrage ${SERVICE_NAME} (migrations EF au boot)..."
ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" bash -s <<REMOTE_HEALTH
set -eu
PORT='${LISTEN_PORT}'
for i in \$(seq 1 45); do
  HTTP_CODE=\$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:\${PORT}/health" 2>/dev/null || echo "000")
  if [ "\${HTTP_CODE}" = "200" ]; then
    echo "Healthcheck /health OK (HTTP 200) après \${i} tentative(s)"
    exit 0
  fi
  if ! systemctl is-active --quiet '${SERVICE_NAME}'; then
    echo "Service inactif (tentative \${i}, HTTP \${HTTP_CODE})"
    journalctl -u '${SERVICE_NAME}' -n 15 --no-pager || true
  elif [ "\${HTTP_CODE}" != "000" ] && [ "\${i}" -ge 3 ]; then
    echo "Healthcheck HTTP \${HTTP_CODE} (tentative \${i})"
  fi
  sleep 2
done
echo "::error::${SERVICE_NAME} ne répond pas sur /health après 90 s"
journalctl -u '${SERVICE_NAME}' -n 30 --no-pager || true
exit 1
REMOTE_HEALTH

echo "Déploiement GISEBS Pay Gateway réussi sur ${SSH_HOST} (${SERVICE_NAME})."
