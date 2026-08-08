using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Transactions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;

    public IndexModel(ApplicationDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public string? StatusFilter { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<TransactionViewModel> Transactions { get; private set; } = [];

    public int PendingCount { get; private set; }

    public record TransactionViewModel(
        Guid Id,
        DateTime CreatedAt,
        string PaymentCode,
        string AppName,
        string CustomerCode,
        string ProductCode,
        decimal Amount,
        decimal? TotalAmount,
        string Currency,
        decimal? TaxAmount,
        decimal? StripeFee,
        decimal? NetAmount,
        string Status,
        DateTime? PaidAt,
        string? InvoiceNumber,
        string? StripePaymentIntentId,
        string? StripeCheckoutSessionId);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        var query = BuildQuery(search, StatusFilter);
        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount, BuildExtraQuery());
        PageNumber = Pagination.Page;

        PendingCount = await BuildQuery(search, "Pending").CountAsync(cancellationToken);

        Transactions = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new TransactionViewModel(
                x.Id,
                x.CreatedAt,
                x.PaymentCode,
                x.ClientApplication.Name,
                x.Customer.CustomerCode,
                x.Product.ProductCode,
                x.Amount,
                x.GrossAmount ?? (x.AmountSubtotal.HasValue && x.TaxAmount.HasValue
                    ? x.AmountSubtotal.Value + x.TaxAmount.Value
                    : (decimal?)null),
                x.Currency,
                x.TaxAmount,
                x.StripeFee,
                x.NetAmount,
                x.Status.ToString(),
                x.PaidAt,
                _db.PaymentInvoices
                    .Where(i => i.PaymentTransactionId == x.Id)
                    .Select(i => i.InvoiceCode)
                    .FirstOrDefault(),
                x.StripePaymentIntentId,
                x.StripeCheckoutSessionId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAllPendingAsync(CancellationToken cancellationToken)
    {
        var query = BuildQuery(Search, "Pending");
        var pending = await query.OrderBy(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            TempData["Error"] = "Aucune transaction Pending à supprimer.";
            return RedirectToPage(new { page = PageNumber, search = Search, status = StatusFilter });
        }

        var ids = pending.Select(x => x.Id).ToList();

        var invoices = await _db.PaymentInvoices
            .Where(x => x.PaymentTransactionId != null && ids.Contains(x.PaymentTransactionId.Value))
            .ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
            invoice.PaymentTransactionId = null;

        var taxes = await _db.CollectedTaxRecords
            .Where(x => x.PaymentTransactionId != null && ids.Contains(x.PaymentTransactionId.Value))
            .ToListAsync(cancellationToken);
        foreach (var tax in taxes)
            tax.PaymentTransactionId = null;

        _db.PaymentTransactions.RemoveRange(pending);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PaymentTransactionsBulkDeleted",
            nameof(PaymentTransaction),
            null,
            true,
            $"Count={pending.Count}; Status=Pending",
            userName: User.Identity?.Name);

        TempData["Success"] = $"{pending.Count} transaction(s) Pending supprimée(s).";
        return RedirectToPage(new { search = Search, status = StatusFilter });
    }

    private IQueryable<PaymentTransaction> BuildQuery(string? search, string? statusFilter)
    {
        IQueryable<PaymentTransaction> query = _db.PaymentTransactions
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product);

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<PaymentStatus>(statusFilter, ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.PaymentCode, $"%{search}%") ||
                EF.Functions.ILike(x.ClientApplication.Name, $"%{search}%") ||
                EF.Functions.ILike(x.Customer.CustomerCode, $"%{search}%") ||
                EF.Functions.ILike(x.Product.ProductCode, $"%{search}%") ||
                EF.Functions.ILike(x.Status.ToString(), $"%{search}%") ||
                (x.StripePaymentIntentId != null && EF.Functions.ILike(x.StripePaymentIntentId, $"%{search}%")) ||
                (x.StripeCheckoutSessionId != null && EF.Functions.ILike(x.StripeCheckoutSessionId, $"%{search}%")) ||
                _db.PaymentInvoices.Any(i => i.PaymentTransactionId == x.Id && EF.Functions.ILike(i.InvoiceCode, $"%{search}%")));
        }

        return query;
    }

    private string? BuildExtraQuery()
    {
        if (string.IsNullOrWhiteSpace(StatusFilter))
            return null;
        return $"status={Uri.EscapeDataString(StatusFilter)}";
    }
}
