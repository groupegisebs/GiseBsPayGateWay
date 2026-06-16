using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Invoices;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

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
        Invoices = await _db.PaymentInvoices.AsNoTracking()
            .Include(x => x.ClientApplication)
            .OrderByDescending(x => x.InvoiceDate)
            .Take(200)
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
