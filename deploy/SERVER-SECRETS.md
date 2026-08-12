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

Contenu (clés **Live** = prod par défaut ; clés **Test** = si la requête envoie `X-Stripe-Env: DEV`) :

```json
{
  "Stripe": {
    "Live": {
      "PublishableKey": "pk_live_...",
      "SecretKey": "sk_live_...",
      "WebhookSecret": "whsec_..."
    },
    "Test": {
      "PublishableKey": "pk_test_...",
      "SecretKey": "sk_test_...",
      "WebhookSecret": "whsec_..."
    }
  },
  "Jwt": {
    "SecretKey": "votre-cle-jwt-aleatoire-minimum-32-caracteres"
  }
}
```

Compatibilité : l’ancien format plat (`PublishableKey` / `SecretKey` au niveau `Stripe`) reste lu comme **Live**.

### Mode DEV vs PROD (par requête)

| Requête | Secrets utilisés |
|---------|------------------|
| Pas de header (ou autre valeur) | **Live** (production) |
| Header `X-Stripe-Env: DEV` (ou `TEST`) | **Test** (bac à sable) |

Exemple checkout test :

```http
POST /api/payments/checkout
X-App-Code: BOUTIQUEGISE
X-Api-Key: gbsk_...
X-Stripe-Env: DEV
Content-Type: application/json
```

Sans `X-Stripe-Env` → production.

Configurez **deux** endpoints webhook Stripe (live + test) vers la même URL : le gateway essaie les deux secrets de signature.

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
   - Mode **Production** → `Stripe:Live` (`pk_live_` / `sk_live_`)
   - Mode **Test** → `Stripe:Test` (`pk_test_` / `sk_test_`)
2. **Développeurs** → **Webhooks** → **deux** endpoints (mode **Test** + mode **Live**) vers la même URL :
   `https://gisebsapipaygateway.gisebs.com/api/webhooks/stripe`
   - Endpoint **Test** → coller le `whsec_…` dans `Stripe:Test:WebhookSecret`
   - Endpoint **Live** → coller le `whsec_…` dans `Stripe:Live:WebhookSecret`

### Événements à cocher (obligatoires)

Le gateway ne traite que ces types (les autres répondent **200 Ignored** sans mettre à jour le paiement) :

| Événement Stripe | Rôle |
|------------------|------|
| `checkout.session.completed` | **Principal** — marque le paiement Succeeded |
| `checkout.session.async_payment_succeeded` | Paiements différés (ex. virement) |
| `checkout.session.async_payment_failed` | Échec paiement différé |
| `payment_intent.succeeded` | Secours one-shot |
| `payment_intent.payment_failed` | Échec |
| `invoice.paid` | Renouvellements d’abonnement |
| `invoice.payment_failed` | Échec facture |
| `customer.subscription.created` | Lien abonnement + finalisation |
| `customer.subscription.updated` | Statut abonnement |
| `customer.subscription.deleted` | Résiliation |

> **Attention :** `invoice_payment.paid` (avec underscore) **n’est pas** traité. Il faut bien `invoice.paid`. Un 200 sur `invoice_payment.paid` signifie « reçu mais ignoré ».

Dans Stripe Dashboard → endpoint → **Événements** → sélectionner la liste ci-dessus (ou « Select events » / Listen to events).

Les apps clientes envoient `X-Stripe-Env: DEV` uniquement pour les environnements de développement / QA.

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
