using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoiceLinkBuilder _invoiceLinkBuilder;

    public InvoicesController(
        ApplicationDbContext db,
        IInvoiceService invoiceService,
        IInvoiceLinkBuilder invoiceLinkBuilder)
    {
        _db = db;
        _invoiceService = invoiceService;
        _invoiceLinkBuilder = invoiceLinkBuilder;
    }

    [HttpGet("{invoiceNumber}")]
    public async Task<ActionResult<InvoiceResponse>> GetInvoice(string invoiceNumber, CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var invoice = await _db.PaymentInvoices.AsNoTracking()
            .Include(x => x.PaymentTransaction)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.InvoiceCode == invoiceNumber,
                cancellationToken);

        if (invoice is null)
        {
            return NotFound(new ApiErrorResponse("Facture introuvable.", null));
        }

        return Ok(MapInvoice(invoice));
    }

    [HttpGet("{invoiceNumber}/download")]
    public async Task<IActionResult> DownloadInvoice(string invoiceNumber, CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var invoice = await _db.PaymentInvoices
            .Include(x => x.PaymentTransaction)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.InvoiceCode == invoiceNumber,
                cancellationToken);

        if (invoice is null)
        {
            return NotFound(new ApiErrorResponse("Facture introuvable.", null));
        }

        var pdf = await _invoiceService.GetPdfAsync(invoice, cancellationToken);
        if (pdf is null && invoice.PaymentTransaction is not null)
        {
            await _invoiceService.EnsureInvoiceForPaymentAsync(invoice.PaymentTransaction, cancellationToken);
            pdf = await _invoiceService.GetPdfAsync(invoice, cancellationToken);
        }

        if (pdf is null)
        {
            return NotFound(new ApiErrorResponse("PDF de facture indisponible.", null));
        }

        return File(pdf.Value.Content, "application/pdf", pdf.Value.FileName);
    }

    private InvoiceResponse MapInvoice(Entities.PaymentInvoice invoice) =>
        new(
            invoice.InvoiceCode,
            invoice.Status.ToString(),
            invoice.Amount,
            invoice.Currency,
            invoice.InvoiceDate,
            invoice.PaidAt,
            invoice.PaymentTransaction?.PaymentCode,
            invoice.StripePaymentIntentId,
            invoice.StripeCheckoutSessionId,
            invoice.StripeInvoiceId,
            _invoiceLinkBuilder.BuildDownloadUrl(invoice.InvoiceCode),
            invoice.AmountSubtotal,
            invoice.TaxAmount,
            invoice.GrossAmount,
            invoice.StripeFee,
            invoice.NetAmount,
            invoice.StripeBalanceTransactionId,
            invoice.BillingCountry,
            invoice.BillingState);
}
