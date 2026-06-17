using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.CollectedTaxes;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<CollectedTaxViewModel> Records { get; private set; } = [];

    public record CollectedTaxViewModel(
        DateTime CollectedAt,
        string PaymentCode,
        string TransactionReference,
        string AppName,
        string? BillingCountry,
        string? BillingState,
        string? BillingCity,
        decimal AmountSubtotal,
        decimal TaxAmountTotal,
        decimal GrossAmount,
        string Currency,
        string TaxSummary,
        string? StripeTaxTransactionId);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        IQueryable<CollectedTaxRecord> query = _db.CollectedTaxRecords.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Lines);

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.PaymentCode, $"%{search}%") ||
                EF.Functions.ILike(x.TransactionReference, $"%{search}%") ||
                EF.Functions.ILike(x.ClientApplication.Name, $"%{search}%") ||
                (x.BillingCountry != null && EF.Functions.ILike(x.BillingCountry, $"%{search}%")) ||
                (x.BillingState != null && EF.Functions.ILike(x.BillingState, $"%{search}%")) ||
                (x.BillingCity != null && EF.Functions.ILike(x.BillingCity, $"%{search}%")) ||
                (x.StripeTaxTransactionId != null && EF.Functions.ILike(x.StripeTaxTransactionId, $"%{search}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Records = await query
            .OrderByDescending(x => x.CollectedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new CollectedTaxViewModel(
                x.CollectedAt,
                x.PaymentCode,
                x.TransactionReference,
                x.ClientApplication.Name,
                x.BillingCountry,
                x.BillingState,
                x.BillingCity,
                x.AmountSubtotal,
                x.TaxAmountTotal,
                x.GrossAmount,
                x.Currency,
                string.Join(", ", x.Lines.OrderBy(l => l.SortOrder).Select(l => $"{l.Name} {l.Rate:P2} ({l.Amount:N2})")),
                x.StripeTaxTransactionId))
            .ToListAsync(cancellationToken);
    }
}
