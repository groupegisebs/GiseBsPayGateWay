using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Invoices;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<InvoiceListItem> Invoices { get; private set; } = [];

    public record InvoiceListItem(
        Guid Id,
        string InvoiceCode,
        DateTime InvoiceDate,
        string AppName,
        string CustomerCode,
        string CustomerEmail,
        string ProductName,
        string PlanCode,
        decimal Amount,
        string Currency,
        string Status,
        DateTime? PaidAt,
        bool HasDocument);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(Page, Search);
        Search = search;

        var query = _db.PaymentInvoices.AsNoTracking()
            .Include(x => x.ClientApplication);

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.InvoiceCode, $"%{search}%") ||
                EF.Functions.ILike(x.ClientApplication.Name, $"%{search}%") ||
                EF.Functions.ILike(x.CustomerCode, $"%{search}%") ||
                EF.Functions.ILike(x.CustomerEmail, $"%{search}%") ||
                EF.Functions.ILike(x.ProductName, $"%{search}%") ||
                EF.Functions.ILike(x.PlanCode, $"%{search}%") ||
                EF.Functions.ILike(x.Status.ToString(), $"%{search}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        Page = Pagination.Page;

        Invoices = await query
            .OrderByDescending(x => x.InvoiceDate)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new InvoiceListItem(
                x.Id,
                x.InvoiceCode,
                x.InvoiceDate,
                x.ClientApplication.Name,
                x.CustomerCode,
                x.CustomerEmail,
                x.ProductName,
                x.PlanCode,
                x.Amount,
                x.Currency,
                x.Status.ToString(),
                x.PaidAt,
                x.InvoicePdfUrl != null || x.HostedInvoiceUrl != null || x.ReceiptUrl != null))
            .ToListAsync(cancellationToken);
    }
}
