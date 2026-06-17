using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Invoices;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IInvoiceFileStorage _fileStorage;

    public DetailsModel(ApplicationDbContext db, IInvoiceFileStorage fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
    }

    public PaymentInvoice Invoice { get; private set; } = null!;
    public string AppName { get; private set; } = string.Empty;
    public string? PaymentCode { get; private set; }
    public string? SubscriptionCode { get; private set; }
    public bool HasStoredPdf { get; private set; }

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
        HasStoredPdf = _fileStorage.ResolveFullPath(invoice.StoredPdfRelativePath) is not null;
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _db.PaymentInvoices
            .Include(x => x.PaymentTransaction)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (invoice is null)
        {
            return NotFound();
        }

        var fullPath = _fileStorage.ResolveFullPath(invoice.StoredPdfRelativePath);
        if (fullPath is null)
        {
            return NotFound();
        }

        var content = await System.IO.File.ReadAllBytesAsync(fullPath, cancellationToken);
        return File(content, "application/pdf", $"{invoice.InvoiceCode}.pdf");
    }
}
