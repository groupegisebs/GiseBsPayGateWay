using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Services;

public class CatalogService : ICatalogService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeService _stripeService;
    private readonly IAuditService _auditService;

    public CatalogService(ApplicationDbContext db, IStripeService stripeService, IAuditService auditService)
    {
        _db = db;
        _stripeService = stripeService;
        _auditService = auditService;
    }

    public async Task<ProductResponse> CreateProductAsync(
        ClientApplication app,
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var productCode = NormalizeCode(request.ProductCode);

        if (await _db.Products.AnyAsync(
                x => x.ClientApplicationId == app.Id && x.ProductCode == productCode,
                cancellationToken))
        {
            throw new InvalidOperationException($"Le produit '{productCode}' existe déjà pour cette application.");
        }

        var product = new Product
        {
            ClientApplicationId = app.Id,
            ProductCode = productCode,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.SyncToStripe)
        {
            await SyncProductToStripeInternalAsync(product, cancellationToken);
        }

        await _auditService.LogAsync(
            "ProductCreatedViaApi", nameof(Product), product.Id.ToString(), true, product.ProductCode, app.AppCode);

        return MapProduct(product);
    }

    public async Task<PricingPlanResponse> CreatePlanAsync(
        ClientApplication app,
        string productCode,
        CreatePricingPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await FindActiveProductAsync(app, productCode, cancellationToken);
        var (planCode, billingInterval) = ResolvePlanSelection(request.PlanCode, request.BillingInterval);
        var currency = CatalogOptions.ResolveCurrency(request.Currency);

        if (await _db.PricingPlans.AnyAsync(
                x => x.ProductId == product.Id && x.PlanCode == planCode && x.Currency == currency && x.IsActive,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Un plan actif '{planCode}' en {currency.ToUpperInvariant()} existe déjà pour le produit '{product.ProductCode}'.");
        }

        var plan = new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = planCode,
            Name = request.Name.Trim(),
            Amount = request.Amount,
            Currency = currency,
            BillingInterval = billingInterval,
            IsActive = true
        };

        _db.PricingPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.SyncToStripe)
        {
            await SyncPlanToStripeAsync(product, plan, cancellationToken);
        }

        await _auditService.LogAsync(
            "PricingPlanCreatedViaApi", nameof(PricingPlan), plan.Id.ToString(), true,
            $"{product.ProductCode}/{plan.PlanCode}", app.AppCode);

        return MapPlan(plan);
    }

    public async Task<CatalogItemResponse> CreateCatalogItemAsync(
        ClientApplication app,
        CreateCatalogItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var productCode = NormalizeCode(request.ProductCode);
        var (planCode, billingInterval) = ResolvePlanSelection(request.PlanCode, request.BillingInterval);

        var product = await _db.Products
            .Include(x => x.PricingPlans)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.ProductCode == productCode,
                cancellationToken);

        if (product is null)
        {
            product = new Product
            {
                ClientApplicationId = app.Id,
                ProductCode = productCode,
                Name = request.ProductName.Trim(),
                Description = request.Description?.Trim(),
                IsActive = true
            };
            _db.Products.Add(product);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!product.IsActive)
                product.IsActive = true;

            product.Name = request.ProductName.Trim();
            product.Description = request.Description?.Trim();
            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var currency = CatalogOptions.ResolveCurrency(request.Currency);

        var existingPlan = product.PricingPlans.FirstOrDefault(x =>
            x.PlanCode == planCode && x.Currency == currency && x.IsActive);

        if (existingPlan is not null
            && existingPlan.Amount == request.Amount
            && existingPlan.BillingInterval == billingInterval
            && string.Equals(existingPlan.Name, request.PlanName.Trim(), StringComparison.Ordinal))
        {
            if (request.SyncToStripe)
            {
                await SyncProductToStripeInternalAsync(product, cancellationToken);
                await SyncPlanToStripeAsync(product, existingPlan, cancellationToken);
            }

            return new CatalogItemResponse(MapProduct(product, [existingPlan]), MapPlan(existingPlan));
        }

        // Prix Stripe immuables : désactiver l'ancien plan si montant / intervalle / nom changent.
        if (existingPlan is not null)
        {
            existingPlan.IsActive = false;
            existingPlan.UpdatedAt = DateTime.UtcNow;
        }

        var plan = new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = planCode,
            Name = request.PlanName.Trim(),
            Amount = request.Amount,
            Currency = currency,
            BillingInterval = billingInterval,
            IsActive = true
        };

        _db.PricingPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.SyncToStripe)
        {
            await SyncProductToStripeInternalAsync(product, cancellationToken);
            await SyncPlanToStripeAsync(product, plan, cancellationToken);
        }

        await _auditService.LogAsync(
            "CatalogItemCreatedViaApi", nameof(Product), product.Id.ToString(), true,
            $"{product.ProductCode}/{plan.PlanCode}", app.AppCode);

        return new CatalogItemResponse(MapProduct(product, [plan]), MapPlan(plan));
    }

    public async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(
        ClientApplication app,
        CancellationToken cancellationToken = default)
    {
        var products = await _db.Products.AsNoTracking()
            .Include(x => x.PricingPlans)
            .Where(x => x.ClientApplicationId == app.Id && x.IsActive)
            .OrderBy(x => x.ProductCode)
            .ToListAsync(cancellationToken);

        return products.Select(p => MapProduct(p, p.PricingPlans.Where(x => x.IsActive).ToList())).ToList();
    }

    public async Task<ProductResponse?> GetProductAsync(
        ClientApplication app,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(productCode);
        var product = await _db.Products.AsNoTracking()
            .Include(x => x.PricingPlans)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.ProductCode == normalizedCode && x.IsActive,
                cancellationToken);

        return product is null
            ? null
            : MapProduct(product, product.PricingPlans.Where(x => x.IsActive).ToList());
    }

    public async Task<ProductResponse> SyncProductToStripeAsync(
        ClientApplication app,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(x => x.PricingPlans)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.ProductCode == NormalizeCode(productCode) && x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException($"Produit '{NormalizeCode(productCode)}' introuvable.");

        await SyncProductToStripeInternalAsync(product, cancellationToken);

        var activePlans = product.PricingPlans.Where(x => x.IsActive).ToList();
        if (activePlans.Count == 0)
        {
            throw new InvalidOperationException(
                $"Aucun plan actif pour « {product.ProductCode} ». Créez d'abord un plan tarifaire (ex. MONTHLY).");
        }

        foreach (var plan in activePlans)
        {
            await SyncPlanToStripeAsync(product, plan, cancellationToken);
        }

        await _auditService.LogAsync(
            "ProductSyncedToStripe", nameof(Product), product.Id.ToString(), true,
            product.ProductCode, app.AppCode);

        return MapProduct(product, activePlans);
    }

    private async Task<Product> FindActiveProductAsync(
        ClientApplication app,
        string productCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(productCode);
        return await _db.Products.FirstOrDefaultAsync(
                   x => x.ClientApplicationId == app.Id && x.ProductCode == normalizedCode && x.IsActive,
                   cancellationToken)
               ?? throw new InvalidOperationException($"Produit '{normalizedCode}' introuvable.");
    }

    private async Task SyncProductToStripeInternalAsync(Product product, CancellationToken cancellationToken)
    {
        product.StripeProductId = await _stripeService.EnsureStripeProductAsync(product, cancellationToken);
    }

    private async Task SyncPlanToStripeAsync(Product product, PricingPlan plan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(product.StripeProductId))
        {
            await SyncProductToStripeInternalAsync(product, cancellationToken);
        }

        plan.StripePriceId = await _stripeService.EnsureStripePriceAsync(
            plan, product.StripeProductId!, cancellationToken);
    }

    private static (string PlanCode, BillingInterval BillingInterval) ResolvePlanSelection(
        string planCode,
        string? billingInterval)
    {
        var option = CatalogOptions.ResolvePlanCode(planCode);

        if (string.IsNullOrWhiteSpace(billingInterval))
        {
            return (option.Code, option.BillingInterval);
        }

        if (!Enum.TryParse<BillingInterval>(billingInterval.Trim(), true, out var parsed))
        {
            throw new InvalidOperationException(
                $"Intervalle invalide '{billingInterval}'. Valeurs acceptées : OneTime, Monthly, Yearly.");
        }

        if (parsed != option.BillingInterval)
        {
            throw new InvalidOperationException(
                $"Le plan '{option.Code}' correspond à l'intervalle {option.BillingInterval}, pas {parsed}.");
        }

        return (option.Code, parsed);
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Le code est requis.");
        }

        return code.Trim().ToUpperInvariant();
    }

    private static ProductResponse MapProduct(Product product, IReadOnlyList<PricingPlan>? plans = null) =>
        new(
            product.ProductCode,
            product.Name,
            product.Description,
            product.IsActive,
            product.StripeProductId,
            product.CreatedAt,
            plans?.Select(MapPlan).ToList());

    private static PricingPlanResponse MapPlan(PricingPlan plan) =>
        new(
            plan.PlanCode,
            plan.Name,
            plan.Amount,
            plan.Currency,
            plan.BillingInterval.ToString(),
            plan.IsActive,
            plan.StripePriceId,
            plan.CreatedAt);
}
