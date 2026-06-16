# Secrets GitHub — GISEBS Pay Gateway (minimum requis)

**Settings → Secrets and variables → Actions** du dépôt.

---

## 2 secrets obligatoires

| Nom (choisir un des deux noms) | Contenu |
|--------------------------------|---------|
| **`GISEBSPAY_SSH_PRIVATE_KEY`** *(recommandé pour ce dépôt)* | Clé privée SSH complète (multiligne) |
| *ou* `SSH_PRIVATE_KEY_UBUNTU1` | même contenu (secret org partagé) |

| Nom | Contenu |
|-----|---------|
| **`GISEBSPAY_CONNECTION_STRING`** *(recommandé)* | `Host=51.79.53.197;Port=5432;Database=gisebs_pay_gateway;Username=gisedocuser;Password=VOTRE_MDP` |
| *ou* `UBUNTU1_CONNECTION_STRING` | même contenu (secret org partagé) |

---

## Host / User / Port — valeurs par défaut du workflow

| Paramètre | Défaut |
|-----------|--------|
| Host | `51.79.53.197` |
| User | `ubuntu` |
| Port SSH | `22` |
| App root | `/opt/apps/gisebs-pay-gateway` |
| Service | `gisebs-pay-gateway` |
| Port app | `7843` |

Surcharge optionnelle via secrets/variables : `SSH_HOST_UBUNTU1`, `UBUNTU1_APP_ROOT`, `GISEBSPAY_LISTEN_PORT`, etc.

---

## Format clé SSH

```
-----BEGIN OPENSSH PRIVATE KEY-----
b3BlbnNzaC1rZX...
-----END OPENSSH PRIVATE KEY-----
```

Copier **tout** le fichier, pas une seule ligne.

---

## Vérification

Après ajout des secrets : **Actions → Deploy Production → Run workflow** (ou push sur `main`).

L'étape **Diagnose secrets** doit afficher `OK` pour la clé SSH et la connection string.

---

## Checklist premier déploiement

- [ ] `GISEBSPAY_SSH_PRIVATE_KEY` (ou `SSH_PRIVATE_KEY_UBUNTU1`) au dépôt ou org
- [ ] `GISEBSPAY_CONNECTION_STRING` (ou `UBUNTU1_CONNECTION_STRING`) au dépôt ou org
- [ ] Serveur : `sudo mkdir -p /opt/apps/gisebs-pay-gateway && sudo chown ubuntu:ubuntu /opt/apps/gisebs-pay-gateway`
- [ ] Push sur `main` ou déclenchement manuel du workflow
- [ ] NPM : proxy vers port `7843`, SSL Let's Encrypt
- [ ] Stripe : webhook `https://pay.gisebs.com/api/webhooks/stripe`

La base PostgreSQL `gisebs_pay_gateway` est créée automatiquement par le workflow si elle n'existe pas. Les migrations EF s'appliquent au démarrage de l'application.
