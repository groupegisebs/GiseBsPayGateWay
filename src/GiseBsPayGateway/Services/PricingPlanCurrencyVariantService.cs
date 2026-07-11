using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public interface IPricingPlanCurrencyVariantService
{
    /// <summary>
    /// Retourne le plan dans la devise verrouillée du customer Stripe.
    /// Crée automatiquement une variante (montant converti + Price Stripe) si absente.
    /// </summary>
    Task<PricingPlan> ResolveForCheckoutAsync(
        Product product,
        string planCode,
        string? lockedCurrency,
        CancellationToken cancellationToken = default);
}

public class PricingPlanCurrencyVariantService : IPricingPlanCurrencyVariantService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrencyConversionService _conversion;
    private readonly IStripeService _stripeService;
    private readonly IAuditService _auditService;
    private readonly CurrencyConversionOptions _options;
    private readonly ILogger<PricingPlanCurrencyVariantService> _logger;

    public PricingPlanCurrencyVariantService(
        ApplicationDbContext db,
        ICurrencyConversionService conversion,
        IStripeService stripeService,
        IAuditService auditService,
        IOptions<CurrencyConversionOptions> options,
        ILogger<PricingPlanCurrencyVariantService> logger)
    {
        _db = db;
        _conversion = conversion;
        _stripeService = stripeService;
        _auditService = auditService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PricingPlan> ResolveForCheckoutAsync(
        Product product,
        string planCode,
        string? lockedCurrency,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlanCode = CatalogOptions.ResolvePlanCode(planCode).Code;
        var plans = product.PricingPlans
            .Where(x => x.PlanCode == normalizedPlanCode && x.IsActive)
            .OrderBy(x => x.Currency)
            .ToList();

        if (plans.Count == 0)
        {
            throw new InvalidOperationException($"Plan '{normalizedPlanCode}' introuvable.");
        }

        if (string.IsNullOrWhiteSpace(lockedCurrency))
        {
            return plans[0];
        }

        var targetCurrency = CatalogOptions.ResolveCurrency(lockedCurrency);
        var matching = plans.FirstOrDefault(x =>
            string.Equals(x.Currency, targetCurrency, StringComparison.OrdinalIgnoreCase));
        if (matching is not null)
        {
            return matching;
        }

        if (!_options.AutoCreatePlanVariant)
        {
            throw new InvalidOperationException(
                $"Ce client Stripe est facturé en {targetCurrency.ToUpperInvariant()}, " +
                $"mais le plan '{normalizedPlanCode}' n'existe qu'en " +
                $"{string.Join(", ", plans.Select(p => p.Currency.ToUpperInvariant()))}. " +
                "Créez un plan dans cette devise ou activez CurrencyConversion:AutoCreatePlanVariant.");
        }

        var source = plans[0];
        return await CreateVariantAsync(product, source, targetCurrency, cancellationToken);
    }

    private async Task<PricingPlan> CreateVariantAsync(
        Product product,
        PricingPlan source,
        string targetCurrency,
        CancellationToken cancellationToken)
    {
        // Double-check DB in case another request created the variant concurrently.
        var existing = await _db.PricingPlans.FirstOrDefaultAsync(
            x => x.ProductId == product.Id
                 && x.PlanCode == source.PlanCode
                 && x.Currency == targetCurrency
                 && x.IsActive,
            cancellationToken);
        if (existing is not null)
        {
            if (!product.PricingPlans.Any(x => x.Id == existing.Id))
            {
                product.PricingPlans.Add(existing);
            }

            return existing;
        }

        var convertedAmount = _conversion.Convert(source.Amount, source.Currency, targetCurrency);
        if (convertedAmount < 0.01m)
        {
            throw new InvalidOperationException(
                $"Montant converti trop bas ({convertedAmount}) pour créer un plan {targetCurrency.ToUpperInvariant()}.");
        }

        var variant = new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = source.PlanCode,
            Name = $"{source.Name} ({targetCurrency.ToUpperInvariant()})",
            Amount = convertedAmount,
            Currency = targetCurrency,
            BillingInterval = source.BillingInterval,
            IsActive = true
        };

        _db.PricingPlans.Add(variant);
        await _db.SaveChangesAsync(cancellationToken);

        var stripeProductId = await _stripeService.EnsureStripeProductAsync(product, cancellationToken);
        variant.StripePriceId = await _stripeService.EnsureStripePriceAsync(
            variant, stripeProductId, cancellationToken);
        variant.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        product.PricingPlans.Add(variant);

        _logger.LogInformation(
            "Plan variante créé {PlanCode} {FromCurrency}->{ToCurrency} amount {FromAmount}->{ToAmount} pour produit {ProductCode}",
            source.PlanCode,
            source.Currency,
            targetCurrency,
            source.Amount,
            convertedAmount,
            product.ProductCode);

        await _auditService.LogAsync(
            "PricingPlanCurrencyVariantAutoCreated",
            nameof(PricingPlan),
            variant.Id.ToString(),
            true,
            $"{product.ProductCode}/{source.PlanCode}: {source.Currency}->{targetCurrency} {source.Amount}->{convertedAmount}");

        return variant;
    }
}
