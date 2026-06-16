using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Data;

/// <summary>
/// Catalogue PayGateway aligné sur BoutiqueGise (ShopCatalogSeed) — AppCode BOUTIQUEGISE.
/// </summary>
public static class BoutiqueGiseSeeder
{
    public const string AppCode = "BOUTIQUEGISE";
    public const string DevApiKey = "gbsk_boutiquegise_dev_local_2026";
    private const string DevApiKeyName = "BoutiqueGise dev local";

    private sealed record CatalogItem(
        string ProductCode,
        string Name,
        string? Description,
        string PlanCode,
        BillingInterval Interval,
        decimal Amount);

    private static readonly CatalogItem[] Catalog =
    [
        new("METADOC-PRO", "MetaDoc Pro", "Gestion documentaire intelligente pour entreprises.", "ONETIME", BillingInterval.OneTime, 2490m),
        new("WARRANTYSAFE", "WarrantySafe", "Suivi garanties et SAV pour distributeurs.", "ONETIME", BillingInterval.OneTime, 1890m),
        new("GISEBS-PLATFORM", "GISEBS Platform", "Plateforme sécurisée multi-applications pour entreprises.", "ONETIME", BillingInterval.OneTime, 990m),
        new("SUPPORT-STD", "Support GISEBS Standard", "Assistance technique et mises à jour incluses.", "YEARLY", BillingInterval.Yearly, 490m),
        new("CLOUD-HOSTING", "Hébergement Cloud GISEBS", "Hébergement managé de votre application GISEBS.", "MONTHLY", BillingInterval.Monthly, 79m),
        new("CONSULT-DEV", "Prestation développement sur mesure", "Journée consultant GISEBS (8 h).", "ONETIME", BillingInterval.OneTime, 850m),
        new("TRAINING-SEC", "Formation sécurité GISEBS", "Session de formation équipe (demi-journée).", "ONETIME", BillingInterval.OneTime, 650m)
    ];

    public static async Task EnsureAsync(
        ApplicationDbContext db,
        IApiKeyService apiKeyService,
        IAuditService auditService,
        ILogger logger,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        var app = await db.ClientApplications
            .FirstOrDefaultAsync(x => x.AppCode == AppCode, cancellationToken);

        if (app is null)
        {
            app = new ClientApplication
            {
                AppCode = AppCode,
                Name = "Boutique GISEBS",
                Description = "Boutique en ligne GISEBS — logiciels, abonnements et services",
                AllowedDomains = "localhost,127.0.0.1,giseboutique.gisebs.com",
                IsActive = true
            };
            db.ClientApplications.Add(app);
            await db.SaveChangesAsync(cancellationToken);
            await auditService.LogAsync("ClientApplicationCreated", nameof(ClientApplication), app.Id.ToString(), true, AppCode);
            logger.LogInformation("Application cliente {AppCode} créée.", AppCode);
        }

        if (isDevelopment)
            await EnsureDevApiKeyAsync(db, apiKeyService, auditService, logger, app, cancellationToken);
        else if (!await db.ApplicationApiKeys.AnyAsync(x => x.ClientApplicationId == app.Id && x.IsActive, cancellationToken))
        {
            var (rawKey, prefix, hash) = apiKeyService.GenerateApiKey();
            db.ApplicationApiKeys.Add(new ApplicationApiKey
            {
                ClientApplicationId = app.Id,
                Name = "Clé par défaut",
                KeyPrefix = prefix,
                KeyHash = hash,
                IsActive = true
            });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Clé API production pour {AppCode} — prefix={Prefix} — CONSULTEZ LES LOGS UNE SEULE FOIS: {RawKey}",
                AppCode, prefix, rawKey);
        }

        foreach (var item in Catalog)
        {
            var product = await db.Products
                .Include(x => x.PricingPlans)
                .FirstOrDefaultAsync(
                    x => x.ClientApplicationId == app.Id && x.ProductCode == item.ProductCode,
                    cancellationToken);

            if (product is null)
            {
                product = new Product
                {
                    ClientApplicationId = app.Id,
                    ProductCode = item.ProductCode,
                    Name = item.Name,
                    Description = item.Description,
                    IsActive = true
                };
                db.Products.Add(product);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                product.Name = item.Name;
                product.Description = item.Description;
                product.IsActive = true;
                product.UpdatedAt = DateTime.UtcNow;
            }

            var plan = product.PricingPlans.FirstOrDefault(x => x.PlanCode == item.PlanCode);
            if (plan is null)
            {
                db.PricingPlans.Add(new PricingPlan
                {
                    ProductId = product.Id,
                    PlanCode = item.PlanCode,
                    Name = item.PlanCode switch
                    {
                        "MONTHLY" => "Mensuel",
                        "YEARLY" => "Annuel",
                        _ => "Paiement unique"
                    },
                    Currency = "eur",
                    Amount = item.Amount,
                    BillingInterval = item.Interval,
                    IsActive = true
                });
            }
            else
            {
                plan.Amount = item.Amount;
                plan.BillingInterval = item.Interval;
                plan.IsActive = true;
                plan.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Catalogue {AppCode} synchronisé ({Count} produits).", AppCode, Catalog.Length);
    }

    private static async Task EnsureDevApiKeyAsync(
        ApplicationDbContext db,
        IApiKeyService apiKeyService,
        IAuditService auditService,
        ILogger logger,
        ClientApplication app,
        CancellationToken cancellationToken)
    {
        var hash = apiKeyService.HashApiKey(DevApiKey);
        var prefix = DevApiKey[..Math.Min(12, DevApiKey.Length)];
        var devKey = await db.ApplicationApiKeys
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.Name == DevApiKeyName, cancellationToken);

        if (devKey is null)
        {
            db.ApplicationApiKeys.Add(new ApplicationApiKey
            {
                ClientApplicationId = app.Id,
                Name = DevApiKeyName,
                KeyPrefix = prefix,
                KeyHash = hash,
                IsActive = true
            });
            await db.SaveChangesAsync(cancellationToken);
            await auditService.LogAsync("DevApiKeyCreated", nameof(ApplicationApiKey), app.Id.ToString(), true,
                $"App={AppCode}; Key={DevApiKeyName}", AppCode);
        }
        else if (!apiKeyService.VerifyApiKey(DevApiKey, devKey.KeyHash))
        {
            devKey.KeyHash = hash;
            devKey.KeyPrefix = prefix;
            devKey.IsActive = true;
            devKey.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Mode développement : BoutiqueGise peut utiliser PayGateway:ApiKey={DevApiKey} avec BaseUrl=http://localhost:7843",
            DevApiKey);
    }
}
