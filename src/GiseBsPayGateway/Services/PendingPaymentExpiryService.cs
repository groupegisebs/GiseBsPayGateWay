using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe.Checkout;

namespace GiseBsPayGateway.Services;

public record PendingPaymentExpiryResult(int Cancelled, int Reconciled);

public interface IPendingPaymentExpiryService
{
    Task<PendingPaymentExpiryResult> ExpireAbandonedAsync(CancellationToken cancellationToken = default);
}

public class PendingPaymentExpiryService : IPendingPaymentExpiryService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripePaymentDetailsService _stripePaymentDetailsService;
    private readonly IWebhookService _webhookService;
    private readonly IAuditService _auditService;
    private readonly PendingPaymentExpiryOptions _options;
    private readonly ILogger<PendingPaymentExpiryService> _logger;

    public PendingPaymentExpiryService(
        ApplicationDbContext db,
        IStripePaymentDetailsService stripePaymentDetailsService,
        IWebhookService webhookService,
        IAuditService auditService,
        IOptions<PendingPaymentExpiryOptions> options,
        ILogger<PendingPaymentExpiryService> logger)
    {
        _db = db;
        _stripePaymentDetailsService = stripePaymentDetailsService;
        _webhookService = webhookService;
        _auditService = auditService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PendingPaymentExpiryResult> ExpireAbandonedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _options.ExpiryHours <= 0)
        {
            return new PendingPaymentExpiryResult(0, 0);
        }

        var cutoff = DateTime.UtcNow.AddHours(-_options.ExpiryHours);
        var batchSize = Math.Max(1, _options.BatchSize);

        var stalePayments = await _db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Product)
            .Include(x => x.Customer)
            .Include(x => x.ClientApplication)
            .Where(x => x.Status == PaymentStatus.Pending && x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var cancelled = 0;
        var reconciled = 0;

        foreach (var payment in stalePayments)
        {
            if (payment.Status != PaymentStatus.Pending)
            {
                continue;
            }

            var outcome = await ReconcileOrExpireAsync(payment, cancellationToken);
            if (outcome == ExpiryOutcome.Reconciled)
            {
                reconciled++;
            }
            else if (outcome == ExpiryOutcome.Cancelled)
            {
                cancelled++;
            }
        }

        return new PendingPaymentExpiryResult(cancelled, reconciled);
    }

    private enum ExpiryOutcome
    {
        Skipped,
        Reconciled,
        Cancelled
    }

    private async Task<ExpiryOutcome> ReconcileOrExpireAsync(
        PaymentTransaction payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
        {
            return await MarkCancelledAsync(
                payment,
                "Checkout non démarré (expiré après délai d'attente)",
                cancellationToken);
        }

        Session? session;
        try
        {
            session = await _stripePaymentDetailsService.GetCheckoutSessionAsync(
                payment.StripeCheckoutSessionId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Lecture session Stripe {SessionId} impossible pour {PaymentCode}",
                payment.StripeCheckoutSessionId,
                payment.PaymentCode);
            return ExpiryOutcome.Skipped;
        }

        if (session is null)
        {
            return await MarkCancelledAsync(
                payment,
                "Session Stripe introuvable (expirée)",
                cancellationToken);
        }

        if (await _webhookService.TryCompleteFromCheckoutSessionAsync(payment, session, cancellationToken))
        {
            _logger.LogInformation(
                "Paiement {PaymentCode} réconcilié depuis Stripe (session {SessionId})",
                payment.PaymentCode,
                session.Id);
            return ExpiryOutcome.Reconciled;
        }

        if (StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session))
        {
            return ExpiryOutcome.Skipped;
        }

        var reason = string.Equals(session.Status, "expired", StringComparison.OrdinalIgnoreCase)
            ? "Checkout abandonné (session Stripe expirée)"
            : "Checkout abandonné (non payé après délai d'attente)";

        return await MarkCancelledAsync(payment, reason, cancellationToken);
    }

    private async Task<ExpiryOutcome> MarkCancelledAsync(
        PaymentTransaction payment,
        string reason,
        CancellationToken cancellationToken)
    {
        payment.Status = PaymentStatus.Cancelled;
        payment.FailureReason = reason;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PendingPaymentExpired",
            nameof(PaymentTransaction),
            payment.Id.ToString(),
            true,
            $"PaymentCode={payment.PaymentCode};Reason={reason}",
            payment.ClientApplication?.AppCode);

        _logger.LogInformation(
            "Paiement {PaymentCode} annulé : {Reason}",
            payment.PaymentCode,
            reason);

        return ExpiryOutcome.Cancelled;
    }
}
