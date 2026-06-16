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
├── GiseBsPayGateway.sln
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
- `Subscription` — abonnements mensuels/annuels
- `StripeWebhookEvent` — journal des webhooks
- `AuditLog` — piste d'audit complète
- `AdminUser` — utilisateurs admin (Identity)
- `StripeSettings` — configuration Stripe chiffrée en base

## Endpoints API

| Méthode | Route | Description |
|---------|-------|-------------|
| `POST` | `/api/checkout/session` | Créer une session Stripe Checkout |
| `GET` | `/api/payments/{paymentCode}` | Statut d'un paiement |
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
- `invoice.paid`
- `invoice.payment_failed`
- `customer.subscription.updated`
- `customer.subscription.deleted`

---

## Déploiement Ubuntu (PostgreSQL + systemd + Nginx Proxy Manager)

### 1. Préparer le serveur

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y postgresql postgresql-contrib nginx curl
```

### 2. Installer .NET 10

```bash
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0
```

### 3. PostgreSQL

```bash
sudo -u postgres psql -f /opt/gisebs-pay-gateway/scripts/init-postgresql.sql
```

Mettez à jour le mot de passe dans la chaîne de connexion.

### 4. Publier l'application

```bash
cd /opt/gisebs-pay-gateway/src/GiseBsPayGateway
dotnet publish -c Release -o /var/www/gisebs-pay-gateway
```

### 5. Configuration production

Créez `/var/www/gisebs-pay-gateway/appsettings.Production.json` :

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=127.0.0.1;Port=5432;Database=gisebs_pay_gateway;Username=gisebs_pay;Password=MOT_DE_PASSE_FORT"
  },
  "Jwt": {
    "SecretKey": "GENERER-UNE-CLE-ALEATOIRE-DE-64-CARACTERES-MINIMUM"
  },
  "Seed": {
    "AdminEmail": "admin@gisebs.com",
    "AdminPassword": "MotDePasseAdminFort!"
  }
}
```

```bash
export ASPNETCORE_ENVIRONMENT=Production
cd /var/www/gisebs-pay-gateway
dotnet GiseBsPayGateway.dll -- migrate  # ou: dotnet ef database update depuis la machine de build
```

> Les migrations s'exécutent automatiquement au démarrage via `DbSeeder`.

### 6. Service systemd

Créez `/etc/systemd/system/gisebs-pay-gateway.service` :

```ini
[Unit]
Description=GISEBS Pay Gateway
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/gisebs-pay-gateway
ExecStart=/usr/bin/dotnet /var/www/gisebs-pay-gateway/GiseBsPayGateway.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:7843

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable gisebs-pay-gateway
sudo systemctl start gisebs-pay-gateway
sudo systemctl status gisebs-pay-gateway
```

Logs :

```bash
journalctl -u gisebs-pay-gateway -f
tail -f /var/www/gisebs-pay-gateway/logs/gisebs-pay-gateway-*.log
```

### 7. Nginx Proxy Manager (NPM)

Dans l'interface NPM (généralement port 81) :

1. **Hosts → Proxy Hosts → Add Proxy Host**
2. **Domain Names** : `pay.gisebs.com`
3. **Scheme** : `http`
4. **Forward Hostname/IP** : IP du serveur (ou `127.0.0.1`)
5. **Forward Port** : `7843`
6. **Block Common Exploits** : activé
7. **Websockets Support** : activé (optionnel)
8. Onglet **SSL** :
   - Request a new SSL Certificate (Let's Encrypt)
   - Force SSL + HTTP/2

### 8. Webhook Stripe en production

URL webhook : `https://pay.gisebs.com/api/webhooks/stripe`

Copiez le **Signing secret** dans le dashboard admin → Stripe.

### 9. Sécurité post-déploiement

- [ ] Changer le mot de passe admin seed
- [ ] Régénérer `Jwt:SecretKey`
- [ ] Restreindre PostgreSQL au localhost
- [ ] Configurer un pare-feu (`ufw allow 80,443`)
- [ ] Sauvegardes PostgreSQL automatiques
- [ ] Ne jamais committer `appsettings.Production.json`

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
