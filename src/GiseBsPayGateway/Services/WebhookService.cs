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
    private readonly IInvoiceService _invoiceService;
    private readonly IStripePaymentDetailsService _stripePaymentDetailsService;
    private readonly ICollectedTaxService _collectedTaxService;
    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        ApplicationDbContext db,
        IAuditService auditService,
        IInvoiceService invoiceService,
        IStripePaymentDetailsService stripePaymentDetailsService,
        ICollectedTaxService collectedTaxService,
        IStripeSettingsProvider stripeSettings,
        ILogger<WebhookService> logger)
    {
        _db = db;
        _auditService = auditService;
        _invoiceService = invoiceService;
        _stripePaymentDetailsService = stripePaymentDetailsService;
        _collectedTaxService = collectedTaxService;
        _stripeSettings = stripeSettings;
        _logger = logger;
    }

    public async Task ProcessStripeWebhookAsync(string json, string signatureHeader, CancellationToken cancellationToken = default)
    {
        var settings = await _stripeSettings.GetActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("Stripe non configuré.");

        StripeConfiguration.ApiKey = settings.SecretKey;

        Event stripeEvent;
        try
        {
            // L'endpoint webhook Stripe peut utiliser une api_version antérieure (ex. 2024-06-20)
            // alors que Stripe.net 52 attend 2026-05-27.dahlia — sans ce flag, ConstructEvent lève
            // une StripeException (ligne ~167) traitée à tort comme une signature invalide (401).
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signatureHeader,
                settings.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex) when (
            ex.Message.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Stripe-Signature", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("webhook secret", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Signature webhook Stripe invalide");
            throw new UnauthorizedAccessException("Signature webhook invalide.");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Erreur Stripe lors de la validation du webhook");
            throw;
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
                case EventTypes.CheckoutSessionAsyncPaymentSucceeded:
                    await HandleCheckoutAsyncPaymentSucceededAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.CheckoutSessionAsyncPaymentFailed:
                    await HandleCheckoutAsyncPaymentFailedAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.PaymentIntentSucceeded:
                    await HandlePaymentIntentSucceededAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.PaymentIntentPaymentFailed:
                    await HandlePaymentIntentPaymentFailedAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.InvoicePaid:
                    await HandleInvoicePaidAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.InvoicePaymentFailed:
                    await HandleInvoiceFailedAsync(stripeEvent, cancellationToken);
                    break;
                case EventTypes.CustomerSubscriptionCreated:
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

        var payment = await LoadPaymentForSessionAsync(session, cancellationToken);
        if (payment is null)
        {
            return;
        }

        payment.StripeCheckoutSessionId = session.Id;
        payment.StripePaymentIntentId = session.PaymentIntentId;
        payment.UpdatedAt = DateTime.UtcNow;

        if (!StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session))
        {
            _logger.LogInformation(
                "Session {SessionId} complétée mais non payée (payment_status={PaymentStatus}); en attente de confirmation",
                session.Id,
                session.PaymentStatus);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var resolvedSession = await ResolveCheckoutSessionForFinalizationAsync(session, cancellationToken);
        if (!await CanFinalizePaidCheckoutSessionAsync(resolvedSession, cancellationToken))
        {
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        await FinalizeSuccessfulCheckoutAsync(payment, resolvedSession, cancellationToken);
    }

    public async Task<bool> TryCompleteFromCheckoutSessionAsync(
        Entities.PaymentTransaction payment,
        Session session,
        CancellationToken cancellationToken = default)
    {
        if (payment.Status == PaymentStatus.Succeeded)
        {
            var resolvedSucceededSession = await ResolveCheckoutSessionForFinalizationAsync(session, cancellationToken);
            await FinalizeSuccessfulCheckoutAsync(payment, resolvedSucceededSession, cancellationToken);
            return true;
        }

        if (!StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session))
        {
            return false;
        }

        var resolvedSession = await ResolveCheckoutSessionForFinalizationAsync(session, cancellationToken);
        if (!await CanFinalizePaidCheckoutSessionAsync(resolvedSession, cancellationToken))
        {
            return false;
        }

        await FinalizeSuccessfulCheckoutAsync(payment, resolvedSession, cancellationToken);
        return true;
    }

    private async Task HandleCheckoutAsyncPaymentSucceededAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var session = stripeEvent.Data.Object as Session
            ?? throw new InvalidOperationException("Objet session invalide.");

        var payment = await LoadPaymentForSessionAsync(session, cancellationToken);
        if (payment is null)
        {
            return;
        }

        if (!StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session))
        {
            _logger.LogWarning(
                "async_payment_succeeded reçu pour session {SessionId} avec payment_status={PaymentStatus}",
                session.Id,
                session.PaymentStatus);
            return;
        }

        var resolvedSession = await ResolveCheckoutSessionForFinalizationAsync(session, cancellationToken);
        if (!await CanFinalizePaidCheckoutSessionAsync(resolvedSession, cancellationToken))
        {
            return;
        }

        await FinalizeSuccessfulCheckoutAsync(payment, resolvedSession, cancellationToken);
    }

    private async Task HandleCheckoutAsyncPaymentFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var session = stripeEvent.Data.Object as Session
            ?? throw new InvalidOperationException("Objet session invalide.");

        var payment = await LoadPaymentForSessionAsync(session, cancellationToken);
        if (payment is null)
        {
            return;
        }

        await HandlePaymentFailureAsync(
            payment,
            ResolveTransactionReference(session.PaymentIntentId, session.Id),
            cancellationToken);
    }

    private async Task HandlePaymentIntentSucceededAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent
            ?? throw new InvalidOperationException("Objet PaymentIntent invalide.");

        if (!StripePaymentVerification.IsPaymentIntentSucceeded(paymentIntent.Status))
        {
            return;
        }

        var payment = await FindPaymentByPaymentIntentAsync(paymentIntent, cancellationToken);
        if (payment is null || payment.Status == PaymentStatus.Succeeded)
        {
            return;
        }

        Session? session = null;
        if (!string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
        {
            session = await _stripePaymentDetailsService.GetCheckoutSessionAsync(
                payment.StripeCheckoutSessionId,
                cancellationToken);
        }

        if (session is not null && StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session))
        {
            await FinalizeSuccessfulCheckoutAsync(payment, session, cancellationToken);
            return;
        }

        await FinalizeSuccessfulPaymentIntentAsync(payment, paymentIntent, cancellationToken);
    }

    private async Task HandlePaymentIntentPaymentFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent
            ?? throw new InvalidOperationException("Objet PaymentIntent invalide.");

        var payment = await FindPaymentByPaymentIntentAsync(paymentIntent, cancellationToken);
        if (payment is null)
        {
            return;
        }

        await HandlePaymentFailureAsync(payment, paymentIntent.Id, cancellationToken);
    }

    private async Task FinalizeSuccessfulCheckoutAsync(
        Entities.PaymentTransaction payment,
        Session session,
        CancellationToken cancellationToken)
    {
        session = await EnsureSessionPaymentIntentAsync(session, cancellationToken);

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await _invoiceService.EnrichSuccessfulPaymentFinancialsAsync(payment, session, null, cancellationToken);
            await _collectedTaxService.SaveFromCheckoutCompletedAsync(payment, session, cancellationToken);
            await EnsureGisebsInvoiceAsync(payment, session, cancellationToken);
            return;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            payment.StripeCheckoutSessionId = session.Id;
            payment.StripePaymentIntentId = session.PaymentIntentId;
            payment.Status = PaymentStatus.Succeeded;
            payment.PaidAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            StripeCheckoutFinancials.ApplySessionTaxToPayment(payment, session);
            if (payment.TaxAmount is null or 0 && !string.IsNullOrWhiteSpace(payment.BillingCountry))
            {
                _logger.LogWarning(
                    "Paiement {PaymentCode} sans taxes Stripe (pays={Country}, province={State}, total={Total}, subtotal={Subtotal})",
                    payment.PaymentCode,
                    payment.BillingCountry,
                    payment.BillingState,
                    session.AmountTotal,
                    session.AmountSubtotal);
            }

            var balanceDetails = await _stripePaymentDetailsService.GetBalanceTransactionDetailsAsync(
                session.PaymentIntentId,
                cancellationToken);
            StripeCheckoutFinancials.ApplyBalanceTransactionToPayment(payment, balanceDetails);

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
            await _collectedTaxService.SaveFromCheckoutCompletedAsync(payment, session, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await _invoiceService.EnrichSuccessfulPaymentFinancialsAsync(payment, session, null, cancellationToken);
        await EnsureGisebsInvoiceAsync(payment, session, cancellationToken);
    }

    private async Task EnsureGisebsInvoiceAsync(
        Entities.PaymentTransaction payment,
        Session session,
        CancellationToken cancellationToken)
    {
        await _invoiceService.SaveFromCheckoutCompletedAsync(session, payment, cancellationToken);

        if (payment.PricingPlan.BillingInterval != BillingInterval.OneTime)
        {
            await _invoiceService.EnsureInvoiceForPaymentAsync(payment, cancellationToken);
        }
    }

    private async Task<Session> EnsureSessionPaymentIntentAsync(Session session, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.PaymentIntentId)
            || string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            return session;
        }

        session.PaymentIntentId = await _stripePaymentDetailsService.GetSubscriptionPaymentIntentIdAsync(
            session.SubscriptionId,
            cancellationToken);
        return session;
    }

    private async Task FinalizeSuccessfulPaymentIntentAsync(
        Entities.PaymentTransaction payment,
        PaymentIntent paymentIntent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            payment.StripePaymentIntentId = paymentIntent.Id;
            payment.Status = PaymentStatus.Succeeded;
            payment.PaidAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            var balanceDetails = await _stripePaymentDetailsService.GetBalanceTransactionDetailsAsync(
                paymentIntent.Id,
                cancellationToken);
            StripeCheckoutFinancials.ApplyBalanceTransactionToPayment(payment, balanceDetails);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Session? session = null;
        if (!string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
        {
            session = await _stripePaymentDetailsService.GetCheckoutSessionAsync(
                payment.StripeCheckoutSessionId,
                cancellationToken);
        }

        await _invoiceService.EnrichSuccessfulPaymentFinancialsAsync(payment, session, null, cancellationToken);

        if (session is not null)
        {
            await _collectedTaxService.SaveFromCheckoutCompletedAsync(payment, session, cancellationToken);
            await EnsureGisebsInvoiceAsync(payment, session, cancellationToken);
        }
        else
        {
            await _invoiceService.EnsureInvoiceForPaymentAsync(payment, cancellationToken);
        }
    }

    private async Task HandlePaymentFailureAsync(
        Entities.PaymentTransaction payment,
        string? transactionReference,
        CancellationToken cancellationToken)
    {
        payment.Status = PaymentStatus.Failed;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _collectedTaxService.RemoveForFailedPaymentAsync(
            payment.Id,
            payment.PaymentCode,
            transactionReference,
            cancellationToken);
    }

    private async Task HandleInvoicePaidAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice is null || !StripePaymentVerification.IsInvoicePaid(invoice))
        {
            return;
        }

        var subscriptionId = GetInvoiceSubscriptionId(invoice);
        if (subscriptionId is null)
        {
            await _invoiceService.SaveFromStripeInvoiceAsync(invoice, InvoiceStatus.Paid, cancellationToken);
            await TrySaveCollectedTaxFromInvoiceAsync(invoice, null, cancellationToken);
            return;
        }

        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscriptionId, cancellationToken);

        if (subscription is null)
        {
            await TryFinalizeInitialSubscriptionPaymentAsync(subscriptionId, invoice, cancellationToken);
            subscription = await _db.Subscriptions
                .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscriptionId, cancellationToken);

            await _invoiceService.SaveFromStripeInvoiceAsync(invoice, InvoiceStatus.Paid, cancellationToken);
            var linkedPayment = subscription is not null
                ? await _db.PaymentTransactions.FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id, cancellationToken)
                : null;
            if (linkedPayment is null
                && invoice.Metadata.TryGetValue("payment_code", out var paymentCode))
            {
                linkedPayment = await _db.PaymentTransactions
                    .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode, cancellationToken);
            }

            if (linkedPayment is not null)
            {
                await _invoiceService.EnrichSuccessfulPaymentFinancialsAsync(linkedPayment, null, invoice, cancellationToken);
            }

            await TrySaveCollectedTaxFromInvoiceAsync(invoice, linkedPayment, cancellationToken);
            return;
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = invoice.PeriodStart;
        subscription.CurrentPeriodEnd = invoice.PeriodEnd;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _invoiceService.SaveFromStripeInvoiceAsync(invoice, InvoiceStatus.Paid, cancellationToken);

        var payment = await _db.PaymentTransactions
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id, cancellationToken);
        if (payment is not null)
        {
            await _invoiceService.EnrichSuccessfulPaymentFinancialsAsync(payment, null, invoice, cancellationToken);
        }

        await TrySaveCollectedTaxFromInvoiceAsync(invoice, payment, cancellationToken);
    }

    private async Task HandleInvoiceFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        var subscriptionId = GetInvoiceSubscriptionId(invoice);
        if (subscriptionId is null)
        {
            await _invoiceService.SaveFromStripeInvoiceAsync(invoice!, InvoiceStatus.Failed, cancellationToken);
            await CleanupTaxForFailedInvoiceAsync(invoice!, cancellationToken);
            return;
        }

        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscriptionId, cancellationToken);

        if (subscription is null)
        {
            await _invoiceService.SaveFromStripeInvoiceAsync(invoice!, InvoiceStatus.Failed, cancellationToken);
            await CleanupTaxForFailedInvoiceAsync(invoice!, cancellationToken);
            return;
        }

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _invoiceService.SaveFromStripeInvoiceAsync(invoice!, InvoiceStatus.Failed, cancellationToken);

        var payment = await _db.PaymentTransactions
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id, cancellationToken);
        if (payment is not null)
        {
            await HandlePaymentFailureAsync(
                payment,
                GetPaymentIntentIdFromInvoice(invoice!) ?? invoice!.Id,
                cancellationToken);
        }
        else
        {
            await CleanupTaxForFailedInvoiceAsync(invoice!, cancellationToken);
        }
    }

    private async Task CleanupTaxForFailedInvoiceAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var transactionReference = GetPaymentIntentIdFromInvoice(invoice) ?? invoice.Id;
        string? paymentCode = invoice.Metadata.TryGetValue("payment_code", out var code) ? code : null;

        await _collectedTaxService.RemoveForFailedPaymentAsync(
            null,
            paymentCode,
            transactionReference,
            cancellationToken);
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
            if (stripeEvent.Type is EventTypes.CustomerSubscriptionCreated or EventTypes.CustomerSubscriptionUpdated)
            {
                await TryFinalizeFromStripeSubscriptionAsync(stripeSubscription, cancellationToken);
            }

            return;
        }

        var firstItem = stripeSubscription.Items?.Data?.FirstOrDefault();
        if (firstItem is not null)
        {
            subscription.CurrentPeriodStart = firstItem.CurrentPeriodStart;
            subscription.CurrentPeriodEnd = firstItem.CurrentPeriodEnd;

            var price = firstItem.Price;
            if (price?.UnitAmount is long unitAmount)
            {
                subscription.StripeAmount = unitAmount / 100m;
            }

            if (!string.IsNullOrWhiteSpace(price?.Currency))
            {
                subscription.StripeCurrency = price.Currency.Trim().ToLowerInvariant();
            }
        }

        SubscriptionLifecycle.ApplyStripeStatus(
            subscription,
            stripeSubscription.Status,
            stripeSubscription.CancelAtPeriodEnd,
            stripeSubscription.CanceledAt,
            subscription.CurrentPeriodEnd);

        SubscriptionLifecycle.NormalizeIfPeriodEnded(subscription);

        subscription.StripeSyncedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySaveCollectedTaxFromInvoiceAsync(
        Invoice invoice,
        Entities.PaymentTransaction? payment,
        CancellationToken cancellationToken)
    {
        try
        {
            await _collectedTaxService.SaveFromStripeInvoiceAsync(invoice, payment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'enregistrer les taxes collectées pour la facture {InvoiceId}", invoice.Id);
        }
    }

    private async Task<Entities.PaymentTransaction?> LoadPaymentForSessionAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        if (!session.Metadata.TryGetValue("payment_code", out var paymentCode))
        {
            return null;
        }

        return await _db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode, cancellationToken);
    }

    private async Task<Entities.PaymentTransaction?> FindPaymentByPaymentIntentAsync(
        PaymentIntent paymentIntent,
        CancellationToken cancellationToken)
    {
        var payment = await _db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.StripePaymentIntentId == paymentIntent.Id, cancellationToken);

        if (payment is not null)
        {
            return payment;
        }

        if (paymentIntent.Metadata.TryGetValue("payment_code", out var paymentCode))
        {
            return await _db.PaymentTransactions
                .Include(x => x.PricingPlan)
                .Include(x => x.Customer)
                .Include(x => x.Product)
                .Include(x => x.ClientApplication)
                .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode, cancellationToken);
        }

        return null;
    }

    private async Task<Session> ResolveCheckoutSessionForFinalizationAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        var resolvedSession = await _stripePaymentDetailsService.GetCheckoutSessionAsync(session.Id, cancellationToken)
            ?? session;

        if (string.IsNullOrWhiteSpace(resolvedSession.PaymentIntentId)
            && !string.IsNullOrWhiteSpace(resolvedSession.SubscriptionId))
        {
            resolvedSession.PaymentIntentId = await _stripePaymentDetailsService.GetSubscriptionPaymentIntentIdAsync(
                resolvedSession.SubscriptionId,
                cancellationToken);
        }

        return resolvedSession;
    }

    private async Task<bool> CanFinalizePaidCheckoutSessionAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        if (!StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(session.PaymentIntentId))
        {
            return await IsPaymentIntentConfirmedAsync(session.PaymentIntentId, cancellationToken);
        }

        _logger.LogWarning(
            "Session {SessionId} payée sans PaymentIntent ni abonnement; confirmation impossible",
            session.Id);
        return false;
    }

    private async Task TryFinalizeFromStripeSubscriptionAsync(
        StripeSubscription stripeSubscription,
        CancellationToken cancellationToken)
    {
        if (!IsActiveSubscriptionStatus(stripeSubscription.Status))
        {
            return;
        }

        if (!stripeSubscription.Metadata.TryGetValue("payment_code", out var paymentCode))
        {
            return;
        }

        var payment = await _db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode && x.Status == PaymentStatus.Pending, cancellationToken);

        if (payment is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
        {
            var session = await _stripePaymentDetailsService.GetCheckoutSessionAsync(
                payment.StripeCheckoutSessionId,
                cancellationToken);

            if (session is not null && await TryCompleteFromCheckoutSessionAsync(payment, session, cancellationToken))
            {
                return;
            }
        }

        var paymentIntentId = await _stripePaymentDetailsService.GetSubscriptionPaymentIntentIdAsync(
            stripeSubscription.Id,
            cancellationToken);

        var syntheticSession = new Session
        {
            Id = payment.StripeCheckoutSessionId ?? string.Empty,
            SubscriptionId = stripeSubscription.Id,
            PaymentStatus = "paid",
            PaymentIntentId = paymentIntentId
        };

        await FinalizeSuccessfulCheckoutAsync(payment, syntheticSession, cancellationToken);
    }

    private async Task TryFinalizeInitialSubscriptionPaymentAsync(
        string subscriptionId,
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        if (!StripePaymentVerification.IsInvoicePaid(invoice))
        {
            return;
        }

        string? paymentCode = invoice.Metadata.TryGetValue("payment_code", out var invoiceCode) ? invoiceCode : null;
        StripeSubscription? stripeSubscription = null;

        if (paymentCode is null)
        {
            stripeSubscription = await _stripePaymentDetailsService.GetSubscriptionAsync(subscriptionId, cancellationToken);
            if (stripeSubscription?.Metadata.TryGetValue("payment_code", out var subscriptionCode) == true)
            {
                paymentCode = subscriptionCode;
            }
        }

        if (paymentCode is null)
        {
            return;
        }

        var payment = await _db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode && x.Status == PaymentStatus.Pending, cancellationToken);

        if (payment is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
        {
            var session = await _stripePaymentDetailsService.GetCheckoutSessionAsync(
                payment.StripeCheckoutSessionId,
                cancellationToken);

            if (session is not null && await TryCompleteFromCheckoutSessionAsync(payment, session, cancellationToken))
            {
                return;
            }
        }

        stripeSubscription ??= await _stripePaymentDetailsService.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (stripeSubscription is not null && IsActiveSubscriptionStatus(stripeSubscription.Status))
        {
            await TryFinalizeFromStripeSubscriptionAsync(stripeSubscription, cancellationToken);
        }
    }

    private static bool IsActiveSubscriptionStatus(string? status) =>
        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "trialing", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsPaymentIntentConfirmedAsync(
        string? paymentIntentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return false;
        }

        var intentStatus = await _stripePaymentDetailsService.GetPaymentIntentStatusAsync(
            paymentIntentId,
            cancellationToken);

        if (StripePaymentVerification.IsPaymentIntentSucceeded(intentStatus))
        {
            return true;
        }

        _logger.LogWarning(
            "PaymentIntent {PaymentIntentId} non réussi (status={Status})",
            paymentIntentId,
            intentStatus);
        return false;
    }

    private static string ResolveTransactionReference(string? paymentIntentId, string sessionId) =>
        string.IsNullOrWhiteSpace(paymentIntentId) ? sessionId : paymentIntentId;

    private static string? GetInvoiceSubscriptionId(Invoice? invoice) =>
        invoice?.Parent?.SubscriptionDetails?.SubscriptionId
        ?? invoice?.Parent?.SubscriptionDetails?.Subscription?.Id;

    private static string? GetPaymentIntentIdFromInvoice(Invoice stripeInvoice)
    {
        var payment = stripeInvoice.Payments?.Data?.FirstOrDefault();
        if (payment?.Payment?.PaymentIntentId is { } paymentIntentId)
        {
            return paymentIntentId;
        }

        if (payment?.Payment?.PaymentIntent is { Id: var expandedId })
        {
            return expandedId;
        }

        return null;
    }
}
