# GISEBS Pay Gateway

Service centralisé de paiement **Stripe** réutilisable par les applications GISEBS (HoloTuto, CogniDoc, WarrantySafe, etc.).

Les applications clientes **ne communiquent jamais directement avec Stripe**. Elles appellent GISEBS Pay Gateway avec un **AppCode** et une **API Key**.

## Stack technique

| Composant | Technologie |
|-----------|-------------|
| API | ASP.NET Core 10 Web API |
| Dashboard admin | Razor Pages + ASP.NET Identity |
| Base de données | PostgreSQL + Entity Framework Core |
| Paiements | Stripe.NET SDK |
| Auth clients | API Key (+ JWT optionnel) |
| Auth admin | ASP.NET Identity (cookies) |
| Logs | Serilog (console + fichiers) |
| API docs | Swagger (développement) |
| Rate limiting | AspNetCoreRateLimit |

> **Note :** Le SDK .NET 10 est utilisé (ASP.NET Core 10). Pour cibler .NET 9, modifiez `TargetFramework` dans le `.csproj`.

## Structure du projet

```
GiseBsPayGateWay/
├── GiseBsPayGateway.slnx
├── .github/workflows/         # Deploy Production (push main)
├── deploy/                    # Scripts GHA + déploiement manuel
├── scripts/
│   └── init-postgresql.sql
└── src/GiseBsPayGateway/
    ├── Controllers/Api/       # Endpoints REST
    ├── Data/                  # DbContext + migrations
    ├── Entities/              # Modèles EF Core
    ├── Enums/
    ├── Middleware/            # Authentification API Key
    ├── Pages/Admin/           # Dashboard admin
    ├── Services/              # Stripe, paiements, audit
    └── appsettings.json
```

## Entités principales

- `ClientApplication` — application cliente (HoloTuto, etc.)
- `ApplicationApiKey` — clés API hashées (SHA-256)
- `Customer` — client final par application
- `Product` / `PricingPlan` — catalogue tarifaire
- `PaymentTransaction` — paiement avec code interne unique
- `CollectedTaxRecord` / `CollectedTaxLine` — taxes collectées par paiement (adresse, composantes, réf. Stripe)
- `Subscription` — abonnements mensuels/annuels
- `StripeWebhookEvent` — journal des webhooks
- `AuditLog` — piste d'audit complète
- `AdminUser` — utilisateurs admin (Identity)
- `StripeSettings` — configuration Stripe chiffrée en base

## Endpoints API

| Méthode | Route | Description |
|---------|-------|-------------|
| `POST` | `/api/checkout/session` | Créer une session Stripe Checkout |
| `GET` | `/api/payments/{paymentCode}` | Statut d'un paiement (inclut `taxBreakdown`, adresse) |
| `POST` | `/api/tax/calculate` | Estimation des taxes (Stripe Tax) |
| `GET` | `/api/tax/collected?from=&to=` | Taxes collectées (par application) |
| `GET` | `/api/customers/{customerCode}/subscriptions` | Abonnements d'un client |
| `POST` | `/api/subscriptions/cancel` | Annuler un abonnement |
| `POST` | `/api/webhooks/stripe` | Webhook Stripe (signature vérifiée) |
| `POST` | `/api/auth/token` | Obtenir un JWT (optionnel) |

### Authentification API

Chaque requête (sauf webhooks et `/api/auth/token`) doit inclure :

```http
X-App-Code: HOLOTUTO
X-Api-Key: gbsk_xxxxxxxxxxxxxxxxxxxxxxxx
Content-Type: application/json
```

### Exemple — créer une session Checkout

```bash
curl -X POST https://pay.gisebs.com/api/checkout/session \
  -H "X-App-Code: HOLOTUTO" \
  -H "X-Api-Key: gbsk_votre_cle" \
  -H "Content-Type: application/json" \
  -d '{
    "customerCode": "USER-123",
    "email": "client@example.com",
    "fullName": "Jean Dupont",
    "productCode": "PREMIUM",
    "planCode": "MONTHLY",
    "successUrl": "https://holotuto.com/payment/success",
    "cancelUrl": "https://holotuto.com/payment/cancel"
  }'
```

### Taxes collectées

**Taxes collectées uniquement sur paiement Stripe confirmé (Succeeded/paid).** Aucun enregistrement n'est créé lors de l'estimation (`POST /api/tax/calculate`), à la création de session Checkout, ni sur échec de paiement.

Lors d'un paiement réussi (`checkout.session.completed` avec `payment_status=paid`, `payment_intent.succeeded`, ou `invoice.paid`), Pay Gateway enregistre une fiche `CollectedTaxRecord` :

- date de collecte (`collectedAt`)
- adresse de facturation complète
- montants (sous-total, taxes, total)
- composantes fiscales (code, nom, taux, montant, type) — ex. GST + QST au Québec
- référence transaction Stripe (`transactionReference`, `stripeTaxTransactionId` optionnel)

`GET /api/payments/{paymentCode}` expose `taxBreakdown[]` et `billingAddress` lorsque l'enregistrement existe.

Exemple de composante pour un paiement Québec (100 $ + taxes) :

```json
{
  "paymentCode": "PAY-BOUTIQUEGISE-abc123",
  "taxAmount": 14.98,
  "grossAmount": 114.98,
  "billingCountry": "CA",
  "billingState": "QC",
  "billingAddress": {
    "line1": "1200 rue Edison",
    "city": "Québec",
    "state": "QC",
    "postalCode": "G3K 0P6",
    "country": "CA"
  },
  "taxBreakdown": [
    { "code": "ca_gst", "name": "GST", "rate": 0.05, "amount": 5.00, "type": "federal" },
    { "code": "ca_qst", "name": "QST", "rate": 0.09975, "amount": 9.98, "type": "provincial" }
  ]
}
```

## Démarrage local

### Prérequis

- .NET 10 SDK
- PostgreSQL 15+

### 1. Base de données

```bash
psql -U postgres -f scripts/init-postgresql.sql
```

Ou créez manuellement la base `gisebs_pay_gateway`.

### 2. Configuration

Modifiez `src/GiseBsPayGateway/appsettings.Development.json` :

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=gisebs_pay_gateway_dev;Username=postgres;Password=VOTRE_MOT_DE_PASSE"
  }
}
```

### 3. Migrations

```bash
cd src/GiseBsPayGateway
dotnet ef database update
```

### 4. Lancer l'application

```bash
dotnet run
```

- Accueil : http://localhost:7843
- Swagger : http://localhost:7843/swagger
- Admin : http://localhost:7843/Account/Login

**Compte admin par défaut (seed)** :
- Email : `admin@gisebs.com`
- Mot de passe : `ChangeMe123!`

**Applications seedées** : `HOLOTUTO`, `COGNIDOC`, `WARRANTYSAFE` (clés API générées au premier démarrage — consultez les logs d'audit).

### 5. Configuration Stripe

1. Connectez-vous au dashboard admin
2. Allez dans **Stripe**
3. Saisissez les clés publishable, secret et le webhook secret
4. Configurez le webhook Stripe vers : `https://votre-domaine/api/webhooks/stripe`

Événements recommandés :
- `checkout.session.completed`
- `checkout.session.async_payment_succeeded`
- `checkout.session.async_payment_failed`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `invoice.paid`
- `invoice.payment_failed`
- `customer.subscription.updated`
- `customer.subscription.deleted`

---

## Déploiement production

**Déploiement automatique via GitHub Actions** (push sur `main`), comme SecureMail Gateway et ComptaDoc-PME.

Guide complet : [`deploy/README.md`](deploy/README.md) · Secrets : [`deploy/GITHUB-SECRETS.md`](deploy/GITHUB-SECRETS.md)

| Élément | Valeur |
|---------|--------|
| Workflow | `.github/workflows/deploy-production.yml` |
| Service systemd | `gisebs-pay-gateway` |
| Répertoire app | `/opt/apps/gisebs-pay-gateway/app` |
| Port d'écoute | `7843` (`http://0.0.0.0:7843`) |
| Healthcheck | `GET /health` |
| NPM | Forward port `7843`, scheme **http** + SSL Let's Encrypt |

Secrets minimum au dépôt : `GISEBSPAY_CONNECTION_STRING` + clé SSH (`GISEBSPAY_SSH_PRIVATE_KEY` ou `SSH_PRIVATE_KEY_UBUNTU1` org).

### Déploiement manuel (secours)

```bat
deploy\deploy.bat
```

Ou sur le serveur : `./deploy/deploy.sh`

### Sécurité post-déploiement

- [ ] Changer le mot de passe admin seed
- [ ] Régénérer `Jwt:SecretKey`
- [ ] Configurer Stripe (dashboard admin)
- [ ] Ne jamais committer `appsettings.Production.json` ni `deploy/*.config.json`

---

## Dashboard admin

| Section | Fonction |
|---------|----------|
| Tableau de bord | Revenus, paiements, abonnements |
| Applications | Gestion AppCode + génération API Keys |
| Produits / Plans | Catalogue tarifaire |
| Transactions | Historique des paiements |
| Abonnements | Suivi des abonnements actifs |
| Webhooks | Événements Stripe reçus |
| Stripe | Configuration des clés |
| Audit | Journal de toutes les actions sensibles |

## Licence

Propriété GISEBS — usage interne.
