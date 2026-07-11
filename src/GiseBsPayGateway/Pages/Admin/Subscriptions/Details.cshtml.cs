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
    private readonly IAuditService _auditService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        ApplicationDbContext db,
        IStripeService stripeService,
        IAuditService auditService,
        ILogger<DetailsModel> logger)
    {
        _db = db;
        _stripeService = stripeService;
        _auditService = auditService;
        _logger = logger;
    }

    public Subscription Subscription { get; private set; } = null!;
    public string AppName { get; private set; } = string.Empty;
    public string AppCode { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public string? CustomerName { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string PlanCode { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = string.Empty;
    public decimal PlanAmount { get; private set; }
    public string PlanCurrency { get; private set; } = "eur";
    public IList<RelatedPaymentVm> RelatedPayments { get; private set; } = [];

    public bool CanScheduleCancel { get; private set; }
    public bool CanCancelImmediately { get; private set; }
    public bool CanUndoSchedule { get; private set; }

    public record RelatedPaymentVm(
        Guid Id,
        string PaymentCode,
        string Status,
        decimal Amount,
        string Currency,
        DateTime CreatedAt,
        DateTime? PaidAt);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
            return NotFound();
        return Page();
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
            TempData["Error"] = $"Échec annulation Stripe : {ex.Message}";
        }

        return RedirectToPage(new { id });
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
        StripeCustomerId = subscription.Customer.StripeCustomerId;
        ProductCode = subscription.Product.ProductCode;
        ProductName = subscription.Product.Name;
        PlanCode = subscription.PricingPlan.PlanCode;
        PlanName = subscription.PricingPlan.Name;
        PlanAmount = subscription.PricingPlan.Amount;
        PlanCurrency = subscription.PricingPlan.Currency;

        var activeLike = subscription.Status is SubscriptionStatus.Active
            or SubscriptionStatus.Trialing
            or SubscriptionStatus.PastDue
            or SubscriptionStatus.Unpaid;

        CanScheduleCancel = activeLike && !subscription.CancelAtPeriodEnd;
        CanCancelImmediately = activeLike;
        CanUndoSchedule = activeLike && subscription.CancelAtPeriodEnd;

        RelatedPayments = await _db.PaymentTransactions.AsNoTracking()
            .Where(x => x.SubscriptionId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .Select(x => new RelatedPaymentVm(
                x.Id,
                x.PaymentCode,
                x.Status.ToString(),
                x.Amount,
                x.Currency,
                x.CreatedAt,
                x.PaidAt))
            .ToListAsync(cancellationToken);

        return true;
    }
}
