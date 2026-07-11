using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;
using Stripe;
using SubscriptionEntity = GiseBsPayGateway.Entities.Subscription;
using StripeInvoiceService = Stripe.InvoiceService;
using StripeSubscriptionService = Stripe.SubscriptionService;

namespace GiseBsPayGateway.Services;

public record SubscriptionSyncResult(
    int Synced,
    int Failed,
    int InvoicesSynced,
    string? Message);

public interface ISubscriptionSyncService
{
    Task<SubscriptionSyncResult> RefreshFromStripeAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSyncResult> RefreshManyFromStripeAsync(
        IEnumerable<Guid> subscriptionIds,
        CancellationToken cancellationToken = default);
}

public class SubscriptionSyncService : ISubscriptionSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly IInvoiceService _invoiceService;
    private readonly IAuditService _auditService;
    private readonly ILogger<SubscriptionSyncService> _logger;

    public SubscriptionSyncService(
        ApplicationDbContext db,
        IStripeSettingsProvider stripeSettings,
        IInvoiceService invoiceService,
        IAuditService auditService,
        ILogger<SubscriptionSyncService> logger)
    {
        _db = db;
        _stripeSettings = stripeSettings;
        _invoiceService = invoiceService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<SubscriptionSyncResult> RefreshFromStripeAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        return await RefreshManyFromStripeAsync([subscriptionId], cancellationToken);
    }

    public async Task<SubscriptionSyncResult> RefreshManyFromStripeAsync(
        IEnumerable<Guid> subscriptionIds,
        CancellationToken cancellationToken = default)
    {
        var settings = await _stripeSettings.GetActiveAsync(cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            return new SubscriptionSyncResult(0, 0, 0, "Stripe n'est pas configuré.");
        }

        StripeConfiguration.ApiKey = settings.SecretKey;

        var ids = subscriptionIds.Distinct().ToList();
        var subscriptions = await _db.Subscriptions
            .Include(x => x.ClientApplication)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var synced = 0;
        var failed = 0;
        var invoicesSynced = 0;
        var errors = new List<string>();

        foreach (var subscription in subscriptions)
        {
            try
            {
                var invoiceCount = await SyncOneAsync(subscription, cancellationToken);
                synced++;
                invoicesSynced += invoiceCount;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{subscription.SubscriptionCode}: {ex.Message}");
                _logger.LogWarning(ex, "Échec sync Stripe pour {Code}", subscription.SubscriptionCode);
            }
        }

        var message = failed == 0
            ? $"{synced} abonnement(s) synchronisé(s), {invoicesSynced} facture(s)."
            : $"{synced} OK, {failed} échec(s), {invoicesSynced} facture(s). {string.Join(" | ", errors.Take(3))}";

        return new SubscriptionSyncResult(synced, failed, invoicesSynced, message);
    }

    private async Task<int> SyncOneAsync(SubscriptionEntity subscription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            throw new InvalidOperationException("Pas de Stripe Subscription ID.");
        }

        var stripeSub = await new StripeSubscriptionService().GetAsync(
            subscription.StripeSubscriptionId,
            new SubscriptionGetOptions
            {
                Expand = ["items.data.price", "latest_invoice"]
            },
            cancellationToken: cancellationToken);

        ApplyStripeSubscription(subscription, stripeSub);
        await _db.SaveChangesAsync(cancellationToken);

        var invoiceCount = await SyncInvoicesAsync(subscription.StripeSubscriptionId, cancellationToken);

        await _auditService.LogAsync(
            "SubscriptionRefreshedFromStripe",
            nameof(SubscriptionEntity),
            subscription.Id.ToString(),
            true,
            $"Code={subscription.SubscriptionCode}; Amount={subscription.StripeAmount} {subscription.StripeCurrency}; Status={subscription.Status}; Invoices={invoiceCount}",
            subscription.ClientApplication.AppCode);

        return invoiceCount;
    }

    private static void ApplyStripeSubscription(SubscriptionEntity subscription, Stripe.Subscription stripeSub)
    {
        subscription.Status = stripeSub.Status switch
        {
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Cancelled,
            "unpaid" => SubscriptionStatus.Unpaid,
            "trialing" => SubscriptionStatus.Trialing,
            "incomplete" => SubscriptionStatus.Incomplete,
            "incomplete_expired" => SubscriptionStatus.Cancelled,
            _ => subscription.Status
        };

        subscription.CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd;
        if (stripeSub.CanceledAt.HasValue)
        {
            subscription.CancelledAt = stripeSub.CanceledAt;
        }

        var firstItem = stripeSub.Items?.Data?.FirstOrDefault();
        if (firstItem is not null)
        {
            subscription.CurrentPeriodStart = firstItem.CurrentPeriodStart;
            subscription.CurrentPeriodEnd = firstItem.CurrentPeriodEnd;

            var price = firstItem.Price;
            if (price is not null)
            {
                if (price.UnitAmount.HasValue)
                {
                    subscription.StripeAmount = price.UnitAmount.Value / 100m;
                }

                if (!string.IsNullOrWhiteSpace(price.Currency))
                {
                    subscription.StripeCurrency = price.Currency.Trim().ToLowerInvariant();
                }
            }
        }

        // Fallback: montant sur la dernière facture si le Price n'a pas UnitAmount (ex. tiered)
        if (subscription.StripeAmount is null && stripeSub.LatestInvoice is not null)
        {
            if (stripeSub.LatestInvoice.AmountPaid > 0)
            {
                subscription.StripeAmount = stripeSub.LatestInvoice.AmountPaid / 100m;
            }
            else if (stripeSub.LatestInvoice.Total > 0)
            {
                subscription.StripeAmount = stripeSub.LatestInvoice.Total / 100m;
            }

            if (string.IsNullOrWhiteSpace(subscription.StripeCurrency)
                && !string.IsNullOrWhiteSpace(stripeSub.LatestInvoice.Currency))
            {
                subscription.StripeCurrency = stripeSub.LatestInvoice.Currency.Trim().ToLowerInvariant();
            }
        }

        subscription.StripeSyncedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<int> SyncInvoicesAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        var invoiceService = new StripeInvoiceService();
        var stripeInvoices = await invoiceService.ListAsync(
            new InvoiceListOptions
            {
                Subscription = stripeSubscriptionId,
                Limit = 100
            },
            cancellationToken: cancellationToken);

        var count = 0;
        foreach (var invoice in stripeInvoices)
        {
            var status = invoice.Status?.ToLowerInvariant() switch
            {
                "paid" => InvoiceStatus.Paid,
                "open" or "draft" => InvoiceStatus.Open,
                "void" => InvoiceStatus.Void,
                "uncollectible" => InvoiceStatus.Failed,
                _ => InvoiceStatus.Open
            };

            await _invoiceService.SaveFromStripeInvoiceAsync(invoice, status, cancellationToken);
            count++;
        }

        return count;
    }
}
