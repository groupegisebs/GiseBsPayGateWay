# Secrets serveur — GISEBS Pay Gateway

Les secrets sensibles **ne doivent pas** être dans GitHub. Seuls SSH et la connection string PostgreSQL passent par les secrets GitHub Actions (pour déployer).

**Stripe, JWT et autres clés** vivent dans un fichier **sur le serveur uniquement**.

---

## Où sont stockés les secrets ?

| Secret | Où | GitHub ? |
|--------|-----|----------|
| Clé SSH déploiement | Secret GitHub `SSH_PRIVATE_KEY_UBUNTU1` ou `GISEBSPAY_SSH_PRIVATE_KEY` | Oui (Actions) |
| Connection string PostgreSQL | Secret GitHub `GISEBSPAY_CONNECTION_STRING` | Oui (Actions) → copié dans `app.env` sur le serveur |
| **Stripe** (pk, sk, webhook) | **`/opt/apps/gisebs-pay-gateway/secrets.json`** | **Non** |
| **JWT** (optionnel) | Même fichier `secrets.json` | **Non** |
| Mots de passe admin | Base PostgreSQL (seed initial) | Non |

---

## Étape 1 — Secrets GitHub (déploiement uniquement)

Voir [GITHUB-SECRETS.md](./GITHUB-SECRETS.md) :

1. `SSH_PRIVATE_KEY_UBUNTU1` (org) **ou** `GISEBSPAY_SSH_PRIVATE_KEY`
2. `GISEBSPAY_CONNECTION_STRING` avec `Database=gisebs_pay_gateway`

**Ne mettez pas les clés Stripe dans GitHub.**

---

## Étape 2 — Fichier secrets sur le serveur (une fois)

Connectez-vous en SSH :

```bash
ssh ubuntu@51.79.53.197
```

Créez le fichier à partir de l’exemple du dépôt :

```bash
sudo mkdir -p /opt/apps/gisebs-pay-gateway
sudo cp /opt/apps/gisebs-pay-gateway/app/../deploy/secrets.example.json /opt/apps/gisebs-pay-gateway/secrets.json 2>/dev/null \
  || nano /opt/apps/gisebs-pay-gateway/secrets.json
```

Si le dépôt n’est pas sur le serveur, créez le fichier manuellement :

```bash
nano /opt/apps/gisebs-pay-gateway/secrets.json
```

Contenu (adapter avec vos vraies clés Stripe **test** pour commencer) :

```json
{
  "Stripe": {
    "PublishableKey": "pk_test_...",
    "SecretKey": "sk_test_...",
    "WebhookSecret": "whsec_...",
    "IsLiveMode": false
  },
  "Jwt": {
    "SecretKey": "votre-cle-jwt-aleatoire-minimum-32-caracteres"
  }
}
```

Sécurisez le fichier :

```bash
sudo chown ubuntu:ubuntu /opt/apps/gisebs-pay-gateway/secrets.json
chmod 600 /opt/apps/gisebs-pay-gateway/secrets.json
```

Redémarrez l’application :

```bash
sudo systemctl restart gisebs-pay-gateway
sudo systemctl status gisebs-pay-gateway
```

---

## Étape 3 — Où trouver les clés Stripe

1. [dashboard.stripe.com](https://dashboard.stripe.com) → **Développeurs** → **Clés API**
   - `PublishableKey` → `pk_test_...` ou `pk_live_...`
   - `SecretKey` → `sk_test_...` ou `sk_live_...`
2. **Développeurs** → **Webhooks** → votre endpoint :
   - URL : `https://gisebsapipaygateway.gisebs.com/api/webhooks/stripe`
   - Événements : `checkout.session.completed`, `invoice.paid`, `invoice.payment_failed`, `customer.subscription.updated`, `customer.subscription.deleted`
   - `WebhookSecret` → `whsec_...` (Signing secret)

Mettez `IsLiveMode: true` uniquement avec des clés `pk_live_` / `sk_live_`.

---

## Étape 4 — Stripe Tax (calcul + collecte)

1. [dashboard.stripe.com](https://dashboard.stripe.com) → **Settings** → **Tax** → activer Stripe Tax.
2. **Registrations** : ajoutez vos immatriculations (ex. Canada GST/HST + Québec QST/TVQ).
3. Vérifiez l'endpoint de calcul après déploiement :

```powershell
$body = @{
    billingAddress = @{
        line1      = "1200 rue Edison"
        city       = "Québec"
        state      = "QC"
        postalCode = "G3K 0P6"
        country    = "CA"
    }
    currency         = "cad"
    amountMinorUnits = 10000
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post `
    -Uri "https://gisebsapipaygateway.gisebs.com/api/tax/calculate" `
    -ContentType "application/json" `
    -Headers @{
        "X-App-Code" = "BOUTIQUEGISE"
        "X-Api-Key"  = "gbsk_VOTRE_CLE"
    } `
    -Body $body
```

Attendu : `jurisdictionCode` = `CA-QC`, composantes `gst` + `qst`, `source` = `stripe`.

BoutiqueGisie appelle cet endpoint à l'inscription et à la mise à jour profil ; repli local si indisponible.

---

## Comportement de l’application

1. Au démarrage, l’app charge **`/opt/apps/gisebs-pay-gateway/secrets.json`** (ou le chemin `GISEBSPAY_SECRETS_FILE`).
2. Si `Stripe.SecretKey` est présent dans ce fichier → **priorité au fichier** (page admin Stripe en lecture seule).
3. Sinon → fallback sur la configuration saisie dans l’admin (table PostgreSQL).

Le fichier est **hors** du dossier `app/` : les déploiements GitHub Actions ne l’écrasent jamais.

---

## Chemin personnalisé (optionnel)

Dans `/opt/apps/gisebs-pay-gateway/app/app.env`, ajoutez :

```bash
GISEBSPAY_SECRETS_FILE=/chemin/vers/secrets.json
```

Puis `sudo systemctl restart gisebs-pay-gateway`.

---

## Vérification

```bash
# Le fichier existe et est protégé
ls -la /opt/apps/gisebs-pay-gateway/secrets.json

# L’app démarre sans erreur Stripe
journalctl -u gisebs-pay-gateway -n 30 --no-pager

# Healthcheck
curl -s http://127.0.0.1:7843/health
```

Dans l’admin → **Stripe** : bandeau indiquant que les clés viennent du fichier serveur.

---

## Passage en production (live)

1. Créez les clés **live** dans Stripe.
2. Mettez à jour `secrets.json` sur le serveur (`pk_live_`, `sk_live_`, nouveau `whsec_`, `IsLiveMode: true`).
3. `sudo systemctl restart gisebs-pay-gateway`
4. **Ne commitez jamais** ce fichier.

---

## Checklist

- [ ] GitHub : SSH + `GISEBSPAY_CONNECTION_STRING` seulement
- [ ] Serveur : `/opt/apps/gisebs-pay-gateway/secrets.json` créé (`chmod 600`)
- [ ] Stripe : webhook configuré vers `/api/webhooks/stripe`
- [ ] Stripe Tax activé + immatriculations Canada/QC (si applicable)
- [ ] `POST /api/tax/calculate` testé (adresse QC → CA-QC)
- [ ] Service redémarré
- [ ] Admin Stripe affiche « configuré via fichier serveur »
