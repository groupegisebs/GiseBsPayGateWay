using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Transactions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<TransactionViewModel> Transactions { get; private set; } = [];

    public record TransactionViewModel(
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

        IQueryable<PaymentTransaction> query = _db.PaymentTransactions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product);

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

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Transactions = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new TransactionViewModel(
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
}
