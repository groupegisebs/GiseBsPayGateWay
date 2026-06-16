# Secrets GitHub — GISEBS Pay Gateway

Le workflow **Deploy Production** échoue tant que **2 secrets** ne sont pas configurés.

**Lien direct (secrets du dépôt)** :  
`https://github.com/groupegisebs/GiseBsPayGateWay/settings/secrets/actions`  
*(adaptez l’URL si le dépôt est ailleurs)*

---

## Créer la base PostgreSQL (SSH, une fois)

Connectez-vous au serveur :

```bash
ssh ubuntu@51.79.53.197
```

**Option A — une commande** (si `gisedocuser` existe déjà, comme les autres apps GISEBS) :

```bash
sudo -u postgres psql -v ON_ERROR_STOP=1 -c 'CREATE DATABASE "gisebs_pay_gateway" OWNER gisedocuser;'
sudo -u postgres psql -d gisebs_pay_gateway -c 'GRANT ALL ON SCHEMA public TO gisedocuser;'
```

**Option B — script du dépôt** (après `git pull` sur le serveur) :

```bash
cd /chemin/vers/GiseBsPayGateWay   # ou clone temporaire
sudo -u postgres psql -f deploy/scripts/create-database.sql
```

Les **tables** sont créées par EF Core au premier démarrage de l’app (ou via `dotnet ef database update` en local).

Chaîne pour le secret `GISEBSPAY_CONNECTION_STRING` :

```
Host=51.79.53.197;Port=5432;Database=gisebs_pay_gateway;Username=gisedocuser;Password=VOTRE_MDP
```

---

## Étape 1 — Secret SSH (déjà sur l’org ?)

### Option A — Secret organisation (recommandé si déjà utilisé par GiseMailSender / ComptaDoc)

1. GitHub → **Organisation groupegisebs** → **Settings** → **Secrets and variables** → **Actions**
2. Ouvrir `SSH_PRIVATE_KEY_UBUNTU1`
3. **Repository access** → ajouter **GiseBsPayGateWay** à la liste des dépôts autorisés

Aucun secret à recréer : la clé `cognidoc_deploy` est partagée.

### Option B — Secret propre au dépôt

1. **Settings** du dépôt → **Secrets and variables** → **Actions** → **New repository secret**
2. Nom : `GISEBSPAY_SSH_PRIVATE_KEY`
3. Valeur : contenu complet de la clé privée (`~/.ssh/cognidoc_deploy`)

```
-----BEGIN OPENSSH PRIVATE KEY-----
...
-----END OPENSSH PRIVATE KEY-----
```

---

## Étape 2 — Connection string PostgreSQL (obligatoire, spécifique à ce projet)

Ce secret **doit** être créé pour **GiseBsPayGateWay** (base différente de SecureMail / ComptaDoc).

1. **Settings** du dépôt → **Secrets and variables** → **Actions** → **New repository secret**
2. Nom : **`GISEBSPAY_CONNECTION_STRING`**
3. Valeur (adapter le mot de passe) :

```
Host=51.79.53.197;Port=5432;Database=gisebs_pay_gateway;Username=gisedocuser;Password=VOTRE_MOT_DE_PASSE
```

> **Important** : `Database=gisebs_pay_gateway` — ne pas copier la chaîne de GiseMailSender (`GiseMailSenderService`).

---

## Vérification

1. **Actions** → **Deploy Production** → **Re-run all jobs**
2. L’étape **Diagnose secrets** doit afficher `OK` pour SSH et connection string

| Secret | Statut attendu |
|--------|----------------|
| `SSH_PRIVATE_KEY_UBUNTU1` (org) ou `GISEBSPAY_SSH_PRIVATE_KEY` | OK |
| `GISEBSPAY_CONNECTION_STRING` | OK |

---

## Autres paramètres (déjà dans le workflow, rien à faire)

| Paramètre | Valeur par défaut |
|-----------|-------------------|
| Serveur | `51.79.53.197` |
| User SSH | `ubuntu` |
| App root | `/opt/apps/gisebs-pay-gateway` |
| Service | `gisebs-pay-gateway` |
| Port | `7843` |

---

## Avant le premier déploiement (une fois sur le serveur)

```bash
ssh ubuntu@51.79.53.197
sudo mkdir -p /opt/apps/gisebs-pay-gateway
sudo chown ubuntu:ubuntu /opt/apps/gisebs-pay-gateway
```

La base `gisebs_pay_gateway` est créée automatiquement par le workflow si elle n’existe pas.

---

## Checklist

- [ ] Accès org `SSH_PRIVATE_KEY_UBUNTU1` **ou** secret `GISEBSPAY_SSH_PRIVATE_KEY`
- [ ] Secret `GISEBSPAY_CONNECTION_STRING` avec `Database=gisebs_pay_gateway`
- [ ] Répertoire `/opt/apps/gisebs-pay-gateway` sur le serveur
- [ ] Re-run du workflow Deploy Production
