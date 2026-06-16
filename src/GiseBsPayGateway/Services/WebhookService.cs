using GiseBsPayGateway.Data;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using SubscriptionEntity = GiseBsPayGateway.Entities.Subscription;
using StripeSubscription = Stripe.Subscription;

namespace GiseBsPayGateway.Services;

public class WebhookService : IWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(ApplicationDbContext db, IAuditService auditService, ILogger<WebhookService> logger)
    {
        _db = db;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task ProcessStripeWebhookAsync(string json, string signatureHeader, CancellationToken cancellationToken = default)
    {
        var settings = await _db.StripeSettings.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Stripe non configuré.");

        StripeConfiguration.ApiKey = settings.SecretKey;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, settings.WebhookSecret);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signature webhook Stripe invalide");
            throw new UnauthorizedAccessException("Signature webhook invalide.");
        }

        if (await _db.StripeWebhookEvents.AnyAsync(x => x.StripeEventId == stripeEvent.Id, cancellationToken))
        {
            _logger.LogInformation("Événement Stripe déjà traité: {EventId}", stripeEvent.Id);
            return;
        }

        var webhookEvent = new Entities.StripeWebhookEvent
        {
            StripeEventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            Payload = json,
            ProcessingStatus = WebhookProcessingStatus.Processing
        };

        _db.StripeWebhookEvents.Add(webhookEvent);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            switch (stripeEvent.Type)
            {
                case EventTypes.CheckoutSessionCompleted:
                    await HandleCheckoutCompletedAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.InvoicePaid:
                    await HandleInvoicePaidAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.InvoicePaymentFailed:
                    await HandleInvoiceFailedAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.CustomerSubscriptionUpdated:
                case EventTypes.CustomerSubscriptionDeleted:
                    await HandleSubscriptionChangedAsync(stripeEvent, cancellationToken);
                    break;
                default:
                    webhookEvent.ProcessingStatus = WebhookProcessingStatus.Ignored;
                    break;
            }

            if (webhookEvent.ProcessingStatus == WebhookProcessingStatus.Processing)
            {
                webhookEvent.ProcessingStatus = WebhookProcessingStatus.Processed;
            }

            webhookEvent.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync("StripeWebhookProcessed", nameof(Entities.StripeWebhookEvent), webhookEvent.Id.ToString(), true, stripeEvent.Type);
        }
        catch (Exception ex)
        {
            webhookEvent.ProcessingStatus = WebhookProcessingStatus.Failed;
            webhookEvent.ErrorMessage = ex.Message;
            webhookEvent.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync("StripeWebhookFailed", nameof(Entities.StripeWebhookEvent), webhookEvent.Id.ToString(), false, ex.Message);
            throw;
        }
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var session = stripeEvent.Data.Object as Session
            ?? throw new InvalidOperationException("Objet session invalide.");

        if (!session.Metadata.TryGetValue("payment_code", out var paymentCode))
        {
            return;
        }

        var payment = await _db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode, cancellationToken);

        if (payment is null)
        {
            return;
        }

        payment.StripeCheckoutSessionId = session.Id;
        payment.StripePaymentIntentId = session.PaymentIntentId;
        payment.Status = PaymentStatus.Succeeded;
        payment.PaidAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;

        if (payment.PricingPlan.BillingInterval != BillingInterval.OneTime && !string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            var subscription = new SubscriptionEntity
            {
                ClientApplicationId = payment.ClientApplicationId,
                CustomerId = payment.CustomerId,
                ProductId = payment.ProductId,
                PricingPlanId = payment.PricingPlanId,
                SubscriptionCode = $"SUB-{payment.PaymentCode[4..]}",
                StripeSubscriptionId = session.SubscriptionId,
                Status = SubscriptionStatus.Active
            };
            payment.Subscription = subscription;
            _db.Subscriptions.Add(subscription);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleInvoicePaidAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        var subscriptionId = GetInvoiceSubscriptionId(invoice);
        if (subscriptionId is null)
        {
            return;
        }

        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscriptionId, cancellationToken);

        if (subscription is null)
        {
            return;
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = invoice!.PeriodStart;
        subscription.CurrentPeriodEnd = invoice.PeriodEnd;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleInvoiceFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        var subscriptionId = GetInvoiceSubscriptionId(invoice);
        if (subscriptionId is null)
        {
            return;
        }

        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscriptionId, cancellationToken);

        if (subscription is null)
        {
            return;
        }

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleSubscriptionChangedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var stripeSubscription = stripeEvent.Data.Object as StripeSubscription;
        if (stripeSubscription is null)
        {
            return;
        }

        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == stripeSubscription.Id, cancellationToken);

        if (subscription is null)
        {
            return;
        }

        subscription.Status = stripeSubscription.Status switch
        {
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Cancelled,
            "unpaid" => SubscriptionStatus.Unpaid,
            "trialing" => SubscriptionStatus.Trialing,
            _ => subscription.Status
        };

        var firstItem = stripeSubscription.Items?.Data?.FirstOrDefault();
        if (firstItem is not null)
        {
            subscription.CurrentPeriodStart = firstItem.CurrentPeriodStart;
            subscription.CurrentPeriodEnd = firstItem.CurrentPeriodEnd;
        }

        subscription.CancelAtPeriodEnd = stripeSubscription.CancelAtPeriodEnd;

        if (stripeSubscription.CanceledAt.HasValue)
        {
            subscription.CancelledAt = stripeSubscription.CanceledAt;
        }

        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? GetInvoiceSubscriptionId(Invoice? invoice) =>
        invoice?.Parent?.SubscriptionDetails?.SubscriptionId
        ?? invoice?.Parent?.SubscriptionDetails?.Subscription?.Id;
}
