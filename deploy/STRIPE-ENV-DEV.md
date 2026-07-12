# Pay Gateway — mode Stripe prod vs test

## Principe

Le gateway utilise **par défaut les clés Stripe production**.  
Pour le bac à sable Stripe, l’app cliente doit envoyer un header HTTP.

| Situation | Header | Secrets utilisés |
|-----------|--------|------------------|
| Production / clients réels | **Aucun** (ou autre valeur) | Stripe Live (`Stripe:Live`) |
| Dev / QA / tests | `X-Stripe-Env: DEV` | Stripe Test (`Stripe:Test`) |

Valeur acceptée aussi : `TEST` (équivalent à `DEV`).

## Exemple — checkout production

```http
POST /api/checkout/session
X-App-Code: VOTRE_APP
X-Api-Key: gbsk_...
Content-Type: application/json
```

## Exemple — checkout test

```http
POST /api/checkout/session
X-App-Code: VOTRE_APP
X-Api-Key: gbsk_...
X-Stripe-Env: DEV
Content-Type: application/json
```

## Réponse

Le champ `stripeMode` indique le mode réellement utilisé :

- `"PROD"` → clés live
- `"DEV"` → clés test

## Règles à respecter

1. **Ne jamais** envoyer `X-Stripe-Env: DEV` en production utilisateur.
2. En dev/QA uniquement : ajouter le header (souvent via config / variable d’environnement).
3. Les objets Stripe (customers, prices, subscriptions) sont **séparés** entre test et live — un ID `cus_…` / `price_…` test ne fonctionne pas en prod, et inversement.
4. En mode DEV, utiliser les cartes de test Stripe (`4242 4242 4242 4242`, etc.).

## Exemple d’intégration (recommandé)

```text
SI environnement_app == Development OU Staging
  ALORS ajouter header X-Stripe-Env: DEV
SINON
  NE PAS envoyer le header
```

## Configuration serveur

Les deux jeux de clés sont dans `/opt/apps/gisebs-pay-gateway/secrets.json` :

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
  }
}
```

Deux endpoints webhook Stripe (live + test) pointent vers la même URL ; le gateway valide la signature avec l’un ou l’autre secret.

### Checklist webhook (souvent incomplet)

1. URL : `https://gisebsapipaygateway.gisebs.com/api/webhooks/stripe`
2. **Endpoint Test** + **Endpoint Live** (deux `whsec_…` distincts dans `secrets.json`)
3. Événements cochés au minimum :
   - `checkout.session.completed` ← indispensable pour enregistrer le paiement
   - `checkout.session.async_payment_succeeded` / `failed`
   - `payment_intent.succeeded` / `payment_intent.payment_failed`
   - `invoice.paid` / `invoice.payment_failed` (pas `invoice_payment.paid`)
   - `customer.subscription.created` / `updated` / `deleted`
4. Après paiement test : admin Pay Gateway → **Webhooks** → statut `Processed` (pas `Ignored` / `Failed`)
5. Si paiement reste **Pending** : déployer le correctif mode Test des webhooks, puis **Renvoyer** `checkout.session.completed` depuis Stripe Workbench

Voir aussi [SERVER-SECRETS.md](./SERVER-SECRETS.md).
