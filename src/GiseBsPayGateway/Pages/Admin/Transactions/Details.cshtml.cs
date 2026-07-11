using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Transactions;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(ApplicationDbContext db, IAuditService auditService, ILogger<DetailsModel> logger)
    {
        _db = db;
        _auditService = auditService;
        _logger = logger;
    }

    public PaymentTransaction Transaction { get; private set; } = null!;
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
    public string? InvoiceCode { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public string? SubscriptionCode { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public bool CanDelete { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tx = await LoadAsync(id, cancellationToken);
        if (tx is null)
            return NotFound();

        Map(tx);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tx = await _db.PaymentTransactions
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (tx is null)
            return NotFound();

        if (!IsDeletable(tx.Status))
        {
            TempData["Error"] =
                $"Impossible de supprimer une transaction « {tx.Status} ». Seuls Pending, Cancelled et Failed peuvent être purgés.";
            return RedirectToPage(new { id });
        }

        var paymentCode = tx.PaymentCode;
        var appCode = tx.ClientApplication.AppCode;
        var status = tx.Status.ToString();

        var invoices = await _db.PaymentInvoices
            .Where(x => x.PaymentTransactionId == id)
            .ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
            invoice.PaymentTransactionId = null;

        var taxes = await _db.CollectedTaxRecords
            .Where(x => x.PaymentTransactionId == id)
            .ToListAsync(cancellationToken);
        foreach (var tax in taxes)
            tax.PaymentTransactionId = null;

        _db.PaymentTransactions.Remove(tx);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PaymentTransactionDeleted",
            nameof(PaymentTransaction),
            id.ToString(),
            true,
            $"PaymentCode={paymentCode}; Status={status}",
            appCode,
            userName: User.Identity?.Name);

        _logger.LogWarning(
            "Admin {User} a supprimé la transaction {PaymentCode} ({Status})",
            User.Identity?.Name,
            paymentCode,
            status);

        TempData["Success"] = $"Transaction {paymentCode} supprimée.";
        return RedirectToPage("./Index");
    }

    private async Task<PaymentTransaction?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.PaymentTransactions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    private void Map(PaymentTransaction tx)
    {
        Transaction = tx;
        AppName = tx.ClientApplication.Name;
        AppCode = tx.ClientApplication.AppCode;
        CustomerCode = tx.Customer.CustomerCode;
        CustomerEmail = tx.Customer.Email;
        CustomerName = tx.Customer.FullName;
        StripeCustomerId = tx.Customer.StripeCustomerId;
        ProductCode = tx.Product.ProductCode;
        ProductName = tx.Product.Name;
        PlanCode = tx.PricingPlan.PlanCode;
        PlanName = tx.PricingPlan.Name;
        SubscriptionCode = tx.Subscription?.SubscriptionCode;
        SubscriptionId = tx.SubscriptionId;
        CanDelete = IsDeletable(tx.Status);

        var invoice = _db.PaymentInvoices.AsNoTracking()
            .Where(i => i.PaymentTransactionId == tx.Id)
            .Select(i => new { i.Id, i.InvoiceCode })
            .FirstOrDefault();
        InvoiceId = invoice?.Id;
        InvoiceCode = invoice?.InvoiceCode;
    }

    private static bool IsDeletable(PaymentStatus status) =>
        status is PaymentStatus.Pending or PaymentStatus.Cancelled or PaymentStatus.Failed;
}
