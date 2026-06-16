# Secrets GitHub — GISEBS Pay Gateway

Tous les secrets sensibles sont définis dans **GitHub Actions** (Settings → Secrets and variables → Actions).

À chaque déploiement, le workflow écrit automatiquement :
- `app/app.env` → connection string PostgreSQL
- `/opt/apps/gisebs-pay-gateway/secrets.json` → Stripe + JWT

**Lien direct** :  
`https://github.com/groupegisebs/GiseBsPayGateWay/settings/secrets/actions`

---

## Liste complète des secrets (dépôt GiseBsPayGateWay)

### Déploiement (obligatoires)

| Secret | Obligatoire | Description | Exemple |
|--------|-------------|-------------|---------|
| `SSH_PRIVATE_KEY_UBUNTU1` **ou** `GISEBSPAY_SSH_PRIVATE_KEY` | Oui (1 des 2) | Clé privée SSH pour déployer sur Ubuntu | `-----BEGIN OPENSSH PRIVATE KEY-----...` |
| `GISEBSPAY_CONNECTION_STRING` **ou** `UBUNTU1_CONNECTION_STRING` | Oui (1 des 2) | Connection string PostgreSQL | `Host=51.79.53.197;Port=5432;Database=gisebs_pay_gateway;Username=gisedocuser;Password=...` |

> `Database=gisebs_pay_gateway` — ne pas réutiliser la chaîne d'une autre app.

### Stripe (obligatoires — → `secrets.json` sur le serveur)

| Secret | Description | Où l'obtenir |
|--------|-------------|--------------|
| `GISEBSPAY_STRIPE_PUBLISHABLE_KEY` | Clé publique Stripe | dashboard.stripe.com → Développeurs → Clés API → `pk_test_...` |
| `GISEBSPAY_STRIPE_SECRET_KEY` | Clé secrète Stripe | Même écran → `sk_test_...` |
| `GISEBSPAY_STRIPE_WEBHOOK_SECRET` | Secret webhook | Webhooks → endpoint → Signing secret `whsec_...` |
| `GISEBSPAY_JWT_SECRET_KEY` | Clé JWT interne | Générer : `openssl rand -base64 48` |
| `GISEBSPAY_STRIPE_LIVE_MODE` | Optionnel | `false` (test) ou `true` (production live) — défaut : `false` |

**URL webhook Stripe** : `https://gisebsapipaygateway.gisebs.com/api/webhooks/stripe`

---

## Secrets BoutiqueGisie (dépôt séparé)

Si BoutiqueGisie est déployé via GitHub Actions, ajoutez dans **ce dépôt-là** :

| Secret | Description |
|--------|-------------|
| `BOUTIQUEGISE_PAYGATEWAY_API_KEY` | Clé API `gbsk_...` (app `BOUTIQUEGISE` dans Pay Gateway) |

Dans `appsettings.json` (sans secret) :

```json
"PayGateway": {
  "BaseUrl": "https://gisebsapipaygateway.gisebs.com",
  "AppCode": "BOUTIQUEGISE",
  "RequireHttps": true
}
```

Le workflow injecte `PayGateway__ApiKey` depuis le secret GitHub.

En **local**, utilisez `dotnet user-secrets set "PayGateway:ApiKey" "gbsk_..."`.

---

## Créer les secrets — pas à pas

1. GitHub → **GiseBsPayGateWay** → **Settings** → **Secrets and variables** → **Actions**
2. **New repository secret** pour chaque ligne du tableau ci-dessus
3. **Actions** → **Deploy Production** → **Run workflow**

---

## Vérification (étape Diagnose secrets)

| Secret | Statut attendu |
|--------|----------------|
| SSH | OK |
| Connection string | OK |
| `GISEBSPAY_STRIPE_PUBLISHABLE_KEY` | OK |
| `GISEBSPAY_STRIPE_SECRET_KEY` | OK |
| `GISEBSPAY_STRIPE_WEBHOOK_SECRET` | OK |
| `GISEBSPAY_JWT_SECRET_KEY` | OK |

---

## Paramètres optionnels (défauts dans le workflow)

| Secret / variable | Défaut |
|-------------------|--------|
| `SSH_HOST_UBUNTU1` | `51.79.53.197` |
| `SSH_USER_UBUNTU1` | `ubuntu` |
| `UBUNTU1_APP_ROOT` | `/opt/apps/gisebs-pay-gateway` |
| `GISEBSPAY_LISTEN_PORT` | `7843` |

---

## Checklist

- [ ] `SSH_PRIVATE_KEY_UBUNTU1` ou `GISEBSPAY_SSH_PRIVATE_KEY`
- [ ] `GISEBSPAY_CONNECTION_STRING`
- [ ] `GISEBSPAY_STRIPE_PUBLISHABLE_KEY`
- [ ] `GISEBSPAY_STRIPE_SECRET_KEY`
- [ ] `GISEBSPAY_STRIPE_WEBHOOK_SECRET`
- [ ] `GISEBSPAY_JWT_SECRET_KEY`
- [ ] Webhook Stripe configuré
- [ ] Deploy Production → succès
- [ ] Admin → Stripe : « Clés chargées depuis le fichier serveur »
