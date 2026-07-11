using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Subscriptions;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeService _stripeService;
    private readonly ISubscriptionSyncService _syncService;
    private readonly IAuditService _auditService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        ApplicationDbContext db,
        IStripeService stripeService,
        ISubscriptionSyncService syncService,
        IAuditService auditService,
        ILogger<DetailsModel> logger)
    {
        _db = db;
        _stripeService = stripeService;
        _syncService = syncService;
        _auditService = auditService;
        _logger = logger;
    }

    public Subscription Subscription { get; private set; } = null!;
    public string AppName { get; private set; } = string.Empty;
    public string AppCode { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string PlanCode { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = string.Empty;
    public decimal PlanAmount { get; private set; }
    public string PlanCurrency { get; private set; } = "eur";
    public decimal DisplayAmount { get; private set; }
    public string DisplayCurrency { get; private set; } = "eur";
    public bool AmountFromStripe { get; private set; }
    public DateTime? StripeSyncedAt { get; private set; }
    public IList<PaymentHistoryVm> PaymentHistory { get; private set; } = [];

    public bool CanScheduleCancel { get; private set; }
    public bool CanCancelImmediately { get; private set; }
    public bool CanUndoSchedule { get; private set; }
    public bool CanDeleteLocal { get; private set; } = true;
    public bool CanReactivate { get; private set; }

    public record PaymentHistoryVm(
        DateTime Date,
        string Kind,
        string Reference,
        string LinkPage,
        Guid LinkId,
        decimal Amount,
        decimal? TaxAmount,
        decimal? GrossAmount,
        string Currency,
        string Status,
        DateTime? PaidAt,
        string? Period);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostRefreshFromStripeAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _syncService.RefreshFromStripeAsync(id, cancellationToken);
        if (result.NotFoundOnStripe)
        {
            var deleted = await DeleteLocalSubscriptionAsync(id, "Stripe: abonnement introuvable lors du refresh", cancellationToken);
            if (deleted)
            {
                TempData["Success"] =
                    "Abonnement introuvable sur Stripe — enregistrement local supprimé.";
                return RedirectToPage("./Index");
            }
        }

        TempData[result.Failed > 0 ? "Error" : "Success"] = result.Message;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteLocalAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await DeleteLocalSubscriptionAsync(id, "Suppression manuelle admin", cancellationToken);
        if (!deleted)
            return NotFound();

        TempData["Success"] = "Abonnement local supprimé.";
        return RedirectToPage("./Index");
    }

    public async Task<IActionResult> OnPostReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (subscription is null)
            return NotFound();

        // Annulation programmée mais encore actif → simplement retirer le flag.
        if (subscription.CancelAtPeriodEnd
            && subscription.Status is not SubscriptionStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            {
                TempData["Error"] = "Abonnement Stripe non lié.";
                return RedirectToPage(new { id });
            }

            try
            {
                await _stripeService.SetCancelAtPeriodEndAsync(
                    subscription.StripeSubscriptionId, false, cancellationToken);
                subscription.CancelAtPeriodEnd = false;
                subscription.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                await _auditService.LogAsync(
                    "SubscriptionReactivatedUndoCancel",
                    nameof(Subscription),
                    subscription.Id.ToString(),
                    true,
                    $"Code={subscription.SubscriptionCode}",
                    subscription.ClientApplication.AppCode,
                    userName: User.Identity?.Name);

                TempData["Success"] = "Annulation annulée — l'abonnement reste actif.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reactivate undo failed for {Code}", subscription.SubscriptionCode);
                TempData["Error"] = $"Impossible de réactiver : {ex.Message}";
            }

            return RedirectToPage(new { id });
        }

        if (subscription.Status is not SubscriptionStatus.Cancelled)
        {
            TempData["Error"] = "Seuls les abonnements annulés (ou avec annulation programmée) peuvent être réactivés.";
            return RedirectToPage(new { id });
        }

        try
        {
            var previousStripeId = subscription.StripeSubscriptionId;
            var stripeSub = await _stripeService.RecreateSubscriptionAsync(
                subscription.Customer,
                subscription.Product,
                subscription.PricingPlan,
                previousStripeId,
                cancellationToken);

            subscription.StripeSubscriptionId = stripeSub.Id;
            subscription.Status = stripeSub.Status switch
            {
                "active" => SubscriptionStatus.Active,
                "trialing" => SubscriptionStatus.Trialing,
                "incomplete" => SubscriptionStatus.Incomplete,
                "past_due" => SubscriptionStatus.PastDue,
                _ => SubscriptionStatus.Active
            };
            subscription.CancelAtPeriodEnd = false;
            subscription.CancelledAt = null;

            var firstItem = stripeSub.Items?.Data?.FirstOrDefault();
            if (firstItem is not null)
            {
                subscription.CurrentPeriodStart = firstItem.CurrentPeriodStart;
                subscription.CurrentPeriodEnd = firstItem.CurrentPeriodEnd;
                if (firstItem.Price?.UnitAmount is long unitAmount)
                    subscription.StripeAmount = unitAmount / 100m;
                if (!string.IsNullOrWhiteSpace(firstItem.Price?.Currency))
                    subscription.StripeCurrency = firstItem.Price.Currency.Trim().ToLowerInvariant();
            }

            subscription.StripeSyncedAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "SubscriptionReactivated",
                nameof(Subscription),
                subscription.Id.ToString(),
                true,
                $"Code={subscription.SubscriptionCode}; Previous={previousStripeId}; New={stripeSub.Id}",
                subscription.ClientApplication.AppCode,
                userName: User.Identity?.Name);

            TempData["Success"] =
                $"Abonnement réactivé. Nouvel ID Stripe : {stripeSub.Id} (un abonnement canceled ne peut pas être redémarré — un nouveau a été créé).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reactivate failed for {Code}", subscription.SubscriptionCode);
            TempData["Error"] = $"Réactivation impossible : {ex.Message}";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostScheduleCancelAsync(Guid id, CancellationToken cancellationToken)
    {
        return await CancelInternalAsync(id, cancelImmediately: false, cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelNowAsync(Guid id, CancellationToken cancellationToken)
    {
        return await CancelInternalAsync(id, cancelImmediately: true, cancellationToken);
    }

    public async Task<IActionResult> OnPostUndoScheduleAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (subscription is null)
            return NotFound();

        if (!subscription.CancelAtPeriodEnd || subscription.Status == SubscriptionStatus.Cancelled)
        {
            TempData["Error"] = "Aucune annulation programmée à annuler.";
            return RedirectToPage(new { id });
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            TempData["Error"] = "Abonnement Stripe non lié.";
            return RedirectToPage(new { id });
        }

        try
        {
            await _stripeService.SetCancelAtPeriodEndAsync(subscription.StripeSubscriptionId, false, cancellationToken);
            subscription.CancelAtPeriodEnd = false;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "SubscriptionCancelScheduleUndone",
                nameof(Subscription),
                subscription.Id.ToString(),
                true,
                $"Code={subscription.SubscriptionCode}",
                subscription.ClientApplication.AppCode,
                userName: User.Identity?.Name);

            TempData["Success"] = "Programmation d'annulation retirée — l'abonnement continue.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Undo cancel schedule failed for {Code}", subscription.SubscriptionCode);
            TempData["Error"] = $"Impossible de retirer la programmation : {ex.Message}";
        }

        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> CancelInternalAsync(Guid id, bool cancelImmediately, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (subscription is null)
            return NotFound();

        if (subscription.Status is SubscriptionStatus.Cancelled)
        {
            TempData["Error"] = "Cet abonnement est déjà annulé.";
            return RedirectToPage(new { id });
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            TempData["Error"] = "Abonnement Stripe non lié — impossible d'annuler via Stripe.";
            return RedirectToPage(new { id });
        }

        try
        {
            await _stripeService.CancelSubscriptionAsync(
                subscription.StripeSubscriptionId,
                cancelImmediately,
                cancellationToken);

            subscription.CancelAtPeriodEnd = !cancelImmediately;
            if (cancelImmediately)
            {
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.CancelledAt = DateTime.UtcNow;
            }

            subscription.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                cancelImmediately ? "SubscriptionCancelledImmediate" : "SubscriptionCancelScheduled",
                nameof(Subscription),
                subscription.Id.ToString(),
                true,
                $"Code={subscription.SubscriptionCode}; Immediate={cancelImmediately}; PeriodEnd={subscription.CurrentPeriodEnd:o}",
                subscription.ClientApplication.AppCode,
                userName: User.Identity?.Name);

            TempData["Success"] = cancelImmediately
                ? "Abonnement annulé immédiatement."
                : $"Annulation programmée en fin de période{(subscription.CurrentPeriodEnd.HasValue ? $" ({subscription.CurrentPeriodEnd:g})" : "")}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancel subscription failed for {Code}", subscription.SubscriptionCode);

            if (IsStripeSubscriptionMissing(ex))
            {
                var deleted = await DeleteLocalSubscriptionAsync(
                    id,
                    $"Stripe introuvable lors de l'annulation: {subscription.StripeSubscriptionId}",
                    cancellationToken);
                if (deleted)
                {
                    TempData["Success"] =
                        "Abonnement introuvable sur Stripe — enregistrement local supprimé.";
                    return RedirectToPage("./Index");
                }
            }

            TempData["Error"] = $"Échec annulation Stripe : {ex.Message}";
        }

        return RedirectToPage(new { id });
    }

    private async Task<bool> DeleteLocalSubscriptionAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (subscription is null)
            return false;

        var code = subscription.SubscriptionCode;
        var appCode = subscription.ClientApplication.AppCode;
        var stripeId = subscription.StripeSubscriptionId;

        var payments = await _db.PaymentTransactions
            .Where(x => x.SubscriptionId == id)
            .ToListAsync(cancellationToken);
        foreach (var payment in payments)
        {
            payment.SubscriptionId = null;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        var invoices = await _db.PaymentInvoices
            .Where(x => x.SubscriptionId == id)
            .ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
        {
            invoice.SubscriptionId = null;
            invoice.UpdatedAt = DateTime.UtcNow;
        }

        _db.Subscriptions.Remove(subscription);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "SubscriptionDeletedLocal",
            nameof(Subscription),
            id.ToString(),
            true,
            $"Code={code}; StripeSub={stripeId}; Reason={reason}",
            appCode,
            userName: User.Identity?.Name);

        return true;
    }

    private static bool IsStripeSubscriptionMissing(Exception ex)
    {
        var message = ex.Message;
        if (ex is Stripe.StripeException stripeEx)
        {
            if (string.Equals(stripeEx.StripeError?.Code, "resource_missing", StringComparison.OrdinalIgnoreCase))
                return true;
            message = stripeEx.StripeError?.Message ?? stripeEx.Message;
        }

        return message.Contains("No such subscription", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (subscription is null)
            return false;

        Subscription = subscription;
        AppName = subscription.ClientApplication.Name;
        AppCode = subscription.ClientApplication.AppCode;
        CustomerCode = subscription.Customer.CustomerCode;
        CustomerEmail = subscription.Customer.Email;
        CustomerName = subscription.Customer.FullName;
        CustomerPhone = subscription.Customer.Phone;
        StripeCustomerId = subscription.Customer.StripeCustomerId;
        ProductCode = subscription.Product.ProductCode;
        ProductName = subscription.Product.Name;
        PlanCode = subscription.PricingPlan.PlanCode;
        PlanName = subscription.PricingPlan.Name;
        PlanAmount = subscription.PricingPlan.Amount;
        PlanCurrency = subscription.PricingPlan.Currency;
        AmountFromStripe = subscription.StripeAmount.HasValue;
        DisplayAmount = subscription.StripeAmount ?? subscription.PricingPlan.Amount;
        DisplayCurrency = subscription.StripeCurrency ?? subscription.PricingPlan.Currency;
        StripeSyncedAt = subscription.StripeSyncedAt;

        var activeLike = subscription.Status is SubscriptionStatus.Active
            or SubscriptionStatus.Trialing
            or SubscriptionStatus.PastDue
            or SubscriptionStatus.Unpaid;

        CanScheduleCancel = activeLike && !subscription.CancelAtPeriodEnd;
        CanCancelImmediately = activeLike;
        CanUndoSchedule = activeLike && subscription.CancelAtPeriodEnd;
        CanReactivate = subscription.Status == SubscriptionStatus.Cancelled
                        || (activeLike && subscription.CancelAtPeriodEnd);

        PaymentHistory = await LoadPaymentHistoryAsync(id, cancellationToken);

        return true;
    }

    private async Task<IList<PaymentHistoryVm>> LoadPaymentHistoryAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var invoices = await _db.PaymentInvoices.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.PaidAt ?? x.InvoiceDate)
            .Select(x => new PaymentHistoryVm(
                x.PaidAt ?? x.InvoiceDate,
                "Facture",
                x.InvoiceCode,
                "/Admin/Invoices/Details",
                x.Id,
                x.Amount,
                x.TaxAmount,
                x.GrossAmount,
                x.Currency,
                x.Status.ToString(),
                x.PaidAt,
                x.PeriodStart.HasValue && x.PeriodEnd.HasValue
                    ? $"{x.PeriodStart:d} → {x.PeriodEnd:d}"
                    : null))
            .ToListAsync(cancellationToken);

        var invoicePaymentIds = await _db.PaymentInvoices.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.PaymentTransactionId != null)
            .Select(x => x.PaymentTransactionId!.Value)
            .ToListAsync(cancellationToken);

        var payments = await _db.PaymentTransactions.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.PaidAt ?? x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.PaymentCode,
                x.Status,
                x.Amount,
                x.TaxAmount,
                x.GrossAmount,
                x.Currency,
                x.CreatedAt,
                x.PaidAt
            })
            .ToListAsync(cancellationToken);

        var history = new List<PaymentHistoryVm>(invoices);
        foreach (var payment in payments)
        {
            if (invoicePaymentIds.Contains(payment.Id))
            {
                continue;
            }

            history.Add(new PaymentHistoryVm(
                payment.PaidAt ?? payment.CreatedAt,
                "Paiement",
                payment.PaymentCode,
                "/Admin/Transactions/Details",
                payment.Id,
                payment.Amount,
                payment.TaxAmount,
                payment.GrossAmount,
                payment.Currency,
                payment.Status.ToString(),
                payment.PaidAt,
                null));
        }

        return history
            .OrderByDescending(x => x.Date)
            .ToList();
    }
}
