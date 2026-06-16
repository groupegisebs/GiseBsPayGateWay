# Déploiement GISEBS Pay Gateway

Scripts sur le modèle **ComptaDoc-PME** / **SecureMail Gateway** — configuration via fichiers JSON locaux (non commités).

## Déploiement via GitHub Actions (recommandé)

**Chaque push sur `main` déclenche automatiquement le déploiement** vers UBUNTU1 (`51.79.53.197`).

Workflow : [`.github/workflows/deploy-production.yml`](../.github/workflows/deploy-production.yml)

| Où | Secret / Variable | Valeur |
|----|-------------------|--------|
| Organisation | `SSH_PRIVATE_KEY_UBUNTU1` | Clé `cognidoc_deploy` |
| Organisation | `SSH_HOST_UBUNTU1` | `51.79.53.197` |
| Organisation | `SSH_USER_UBUNTU1` | `ubuntu` |
| Dépôt (secret) | `GISEBSPAY_CONNECTION_STRING` | Chaîne PostgreSQL |
| Dépôt (secret) | `GISEBSPAY_SSH_PRIVATE_KEY` | *(optionnel si org secret)* |

Guide détaillé : [`deploy/GITHUB-SECRETS.md`](GITHUB-SECRETS.md)

Pipeline : publish → SCP → systemd → healthcheck `/health` → migrations EF au boot.

Déclenchement manuel : **Actions → Deploy Production → Run workflow**.

---

## Configuration par projet (déploiement manuel)

| Fichier | Rôle | Commité ? |
|---------|------|-----------|
| `deploy/project.config.example.json` | Modèle applicatif | ✅ |
| `deploy/project.config.json` | **Votre** config (service, port, DLL) | ❌ |
| `deploy/deploy-all.config.example.json` | Modèle serveur SSH | ✅ |
| `deploy/deploy-all.config.json` | **Votre** serveur (IP, user) | ❌ |

### Première configuration

```powershell
copy deploy\project.config.example.json deploy\project.config.json
copy deploy\deploy-all.config.example.json deploy\deploy-all.config.json
```

Valeurs par défaut GISEBS Pay Gateway :

| Clé | Valeur |
|-----|--------|
| `serviceName` | `gisebs-pay-gateway` |
| `appRoot` | `/opt/apps/gisebs-pay-gateway` |
| `listenPort` | `7843` |
| `healthCheckUrl` | `http://localhost:7843/health` |
| `dllName` | `GiseBsPayGateway.dll` |

## Base de données (EF Core)

**Avant le premier déploiement**, créez le schéma avec EF :

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=51.79.53.197;Port=5432;Database=gisebs_pay_gateway;Username=gisedocuser;Password=..."
cd src/GiseBsPayGateway
dotnet ef database update
```

Au démarrage, l'application réapplique les migrations en attente (`MigrateAsync`).

---

## Déploiement manuel (secours)

### Windows → Ubuntu

```bat
deploy\deploy.bat -ServerHost "51.79.53.197"
```

Ou avec `deploy/deploy-all.config.json` configuré :

```bat
deploy\deploy.bat
```

Le script :
1. Publie `src/GiseBsPayGateway/GiseBsPayGateway.csproj`
2. Transfère via SCP
3. Sauvegarde l'ancienne version
4. Installe le service systemd `gisebs-pay-gateway`
5. Vérifie `GET /health`

| Option | Description |
|--------|-------------|
| `-SkipPublish` | Réutilise `./publish` existant |
| `-ConnectionString` | Surcharge la chaîne depuis `appsettings.json` |

### Prérequis SSH (une fois)

```powershell
ssh-keygen -t ed25519 -C "gisebs-pay-deploy" -f $env:USERPROFILE\.ssh\cognidoc_deploy
type $env:USERPROFILE\.ssh\cognidoc_deploy.pub | ssh ubuntu@51.79.53.197 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

Mot de passe BD : préférez **user-secrets** en local :

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Password=..." --project src/GiseBsPayGateway
```

## Déploiement sur le serveur (manuel)

```bash
git pull
chmod +x deploy/deploy.sh
./deploy/deploy.sh
```

## Structure sur le serveur

```
/opt/apps/gisebs-pay-gateway/
├── app/                  # Application publiée
│   ├── app.env           # ConnectionStrings (chmod 600)
│   └── logs/
├── backups/
└── staging/
```

## Première installation serveur

```bash
sudo mkdir -p /opt/apps/gisebs-pay-gateway
sudo chown ubuntu:ubuntu /opt/apps/gisebs-pay-gateway

sudo mkdir -p /opt/apps/gisebs-pay-gateway/app
sudo cp deploy/appsettings.Production.json.example /opt/apps/gisebs-pay-gateway/app/appsettings.Production.json
sudo nano /opt/apps/gisebs-pay-gateway/app/appsettings.Production.json
```

`deploy.sh` **ne remplace jamais** `appsettings.Production.json` s'il existe déjà.

## Service systemd

```bash
sudo systemctl status gisebs-pay-gateway
sudo journalctl -u gisebs-pay-gateway -f
```

Template : `deploy/systemd.service.template`

L'application écoute sur **`http://0.0.0.0:7843`** (toutes interfaces) via `Deployment:ListenPort` ou `UBUNTU1_LISTEN_PORT`.

## Nginx Proxy Manager

Proxy Host → scheme **http** :

| NPM | Forward Host | Forward Port |
|-----|--------------|--------------|
| Natif (même machine) | `127.0.0.1` | `7843` |
| Docker | IP du serveur (`hostname -I`) ou `172.17.0.1` | `7843` |

SSL Let's Encrypt côté NPM (Force SSL).

## Webhook Stripe

URL : `https://pay.gisebs.com/api/webhooks/stripe`

## Dépannage

```bash
curl -v http://localhost:7843/health
sudo journalctl -u gisebs-pay-gateway -e --no-pager
```

## Fichiers deploy/

| Fichier | Rôle |
|---------|------|
| `project.config.example.json` | Config projet |
| `deploy-all.ps1` / `deploy.bat` | Déploiement Windows → Ubuntu |
| `deploy.sh` | Déploiement sur le serveur Linux |
| `deploy-gha.sh` | Déploiement GitHub Actions |
| `gha-env.sh` | Helpers secrets GHA |
| `GITHUB-SECRETS.md` | Guide secrets GitHub |
| `systemd.service.template` | Unit systemd |
| `appsettings.Production.json.example` | Config production |
