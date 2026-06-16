using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Invoices;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public DetailsModel(ApplicationDbContext db) => _db = db;

    public PaymentInvoice Invoice { get; private set; } = null!;
    public string AppName { get; private set; } = string.Empty;
    public string? PaymentCode { get; private set; }
    public string? SubscriptionCode { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _db.PaymentInvoices.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.PaymentTransaction)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (invoice is null)
        {
            return NotFound();
        }

        Invoice = invoice;
        AppName = invoice.ClientApplication.Name;
        PaymentCode = invoice.PaymentTransaction?.PaymentCode;
        SubscriptionCode = invoice.Subscription?.SubscriptionCode;
        return Page();
    }
}
