using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace GiseBsPayGateway.Services;

public interface ICollectedTaxService
{
    Task SaveFromCheckoutCompletedAsync(
        PaymentTransaction payment,
        Session session,
        CancellationToken cancellationToken = default);

    Task SaveFromStripeInvoiceAsync(
        Invoice stripeInvoice,
        PaymentTransaction? payment,
        CancellationToken cancellationToken = default);

    Task<CollectedTaxRecord?> GetByPaymentCodeAsync(
        Guid clientApplicationId,
        string paymentCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectedTaxSummaryDto>> ListCollectedAsync(
        Guid clientApplicationId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);
}

public class CollectedTaxService : ICollectedTaxService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripePaymentDetailsService _stripePaymentDetailsService;
    private readonly ILogger<CollectedTaxService> _logger;

    public CollectedTaxService(
        ApplicationDbContext db,
        IStripePaymentDetailsService stripePaymentDetailsService,
        ILogger<CollectedTaxService> logger)
    {
        _db = db;
        _stripePaymentDetailsService = stripePaymentDetailsService;
        _logger = logger;
    }

    public async Task SaveFromCheckoutCompletedAsync(
        PaymentTransaction payment,
        Session session,
        CancellationToken cancellationToken = default)
    {
        if (await _db.CollectedTaxRecords.AnyAsync(
                x => x.PaymentTransactionId == payment.Id,
                cancellationToken))
        {
            return;
        }

        var lines = CollectedTaxMapper.MapFromCheckoutSession(session);
        if (lines.Count == 0 && payment.TaxAmount is not > 0)
        {
            _logger.LogInformation(
                "Aucune taxe à enregistrer pour le paiement {PaymentCode}",
                payment.PaymentCode);
            return;
        }

        var stripeTaxTransactionId = await _stripePaymentDetailsService.GetStripeTaxTransactionIdAsync(
            session.PaymentIntentId,
            cancellationToken);

        var record = BuildRecord(
            payment.ClientApplicationId,
            payment.Id,
            payment.PaymentCode,
            ResolveTransactionReference(session.PaymentIntentId, session.Id),
            payment.PaidAt ?? DateTime.UtcNow,
            payment.Currency,
            payment.AmountSubtotal ?? (session.AmountSubtotal is > 0 ? session.AmountSubtotal.Value / 100m : 0m),
            payment.TaxAmount ?? (session.TotalDetails?.AmountTax is > 0 ? session.TotalDetails.AmountTax / 100m : 0m),
            payment.GrossAmount ?? (session.AmountTotal is > 0 ? session.AmountTotal.Value / 100m : 0m),
            stripeTaxTransactionId,
            session.CustomerDetails?.Address);

        foreach (var line in lines)
        {
            record.Lines.Add(line);
        }

        if (record.Lines.Count == 0 && record.TaxAmountTotal > 0)
        {
            record.Lines.Add(BuildAggregateLine(record));
        }

        _db.CollectedTaxRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Taxes collectées enregistrées pour {PaymentCode} ({LineCount} composante(s))",
            payment.PaymentCode,
            record.Lines.Count);
    }

    public async Task SaveFromStripeInvoiceAsync(
        Invoice stripeInvoice,
        PaymentTransaction? payment,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stripeInvoice.Id))
        {
            return;
        }

        var transactionReference = GetPaymentIntentIdFromInvoice(stripeInvoice) ?? stripeInvoice.Id;
        if (await _db.CollectedTaxRecords.AnyAsync(
                x => x.TransactionReference == transactionReference,
                cancellationToken))
        {
            return;
        }

        var lines = CollectedTaxMapper.MapFromStripeInvoice(stripeInvoice);
        if (lines.Count == 0)
        {
            return;
        }

        var paymentCode = payment?.PaymentCode
            ?? (stripeInvoice.Metadata.TryGetValue("payment_code", out var code) ? code : $"INV-{stripeInvoice.Id[^8..]}");

        var record = BuildRecord(
            payment?.ClientApplicationId ?? await ResolveClientApplicationIdAsync(payment, stripeInvoice, cancellationToken),
            payment?.Id,
            paymentCode,
            transactionReference,
            stripeInvoice.StatusTransitions?.PaidAt ?? DateTime.UtcNow,
            stripeInvoice.Currency,
            (stripeInvoice.TotalExcludingTax ?? stripeInvoice.Subtotal) / 100m,
            lines.Sum(x => x.Amount),
            stripeInvoice.Total / 100m,
            null,
            stripeInvoice.CustomerAddress);

        foreach (var line in lines)
        {
            record.Lines.Add(line);
        }

        _db.CollectedTaxRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Taxes collectées enregistrées depuis facture Stripe {InvoiceId} ({LineCount} composante(s))",
            stripeInvoice.Id,
            record.Lines.Count);
    }

    public async Task<CollectedTaxRecord?> GetByPaymentCodeAsync(
        Guid clientApplicationId,
        string paymentCode,
        CancellationToken cancellationToken = default)
    {
        return await _db.CollectedTaxRecords.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == clientApplicationId && x.PaymentCode == paymentCode,
                cancellationToken);
    }

    public async Task<IReadOnlyList<CollectedTaxSummaryDto>> ListCollectedAsync(
        Guid clientApplicationId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CollectedTaxRecords.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.ClientApplicationId == clientApplicationId);

        if (from.HasValue)
        {
            query = query.Where(x => x.CollectedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.CollectedAt <= to.Value);
        }

        var records = await query
            .OrderByDescending(x => x.CollectedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        return records.Select(MapSummary).ToList();
    }

    private static CollectedTaxSummaryDto MapSummary(CollectedTaxRecord record) =>
        new(
            record.PaymentCode,
            record.TransactionReference,
            record.CollectedAt,
            record.BillingCountry,
            record.BillingState,
            record.BillingCity,
            record.BillingPostalCode,
            record.AmountSubtotal,
            record.TaxAmountTotal,
            record.GrossAmount,
            record.Currency,
            record.StripeTaxTransactionId,
            CollectedTaxMapper.ToLineDtos(record.Lines),
            CollectedTaxMapper.ToBillingAddressDto(record));

    private static CollectedTaxRecord BuildRecord(
        Guid clientApplicationId,
        Guid? paymentTransactionId,
        string paymentCode,
        string transactionReference,
        DateTime collectedAt,
        string currency,
        decimal amountSubtotal,
        decimal taxAmountTotal,
        decimal grossAmount,
        string? stripeTaxTransactionId,
        Address? billingAddress)
    {
        var record = new CollectedTaxRecord
        {
            ClientApplicationId = clientApplicationId,
            PaymentTransactionId = paymentTransactionId,
            PaymentCode = paymentCode,
            TransactionReference = transactionReference,
            CollectedAt = collectedAt,
            Currency = currency.Trim().ToLowerInvariant(),
            AmountSubtotal = amountSubtotal,
            TaxAmountTotal = taxAmountTotal,
            GrossAmount = grossAmount,
            StripeTaxTransactionId = stripeTaxTransactionId
        };

        CollectedTaxMapper.ApplyBillingAddress(record, billingAddress);
        return record;
    }

    private static CollectedTaxLine BuildAggregateLine(CollectedTaxRecord record)
    {
        var country = record.BillingCountry ?? "XX";
        var rate = record.AmountSubtotal > 0
            ? Math.Round(record.TaxAmountTotal / record.AmountSubtotal, 6, MidpointRounding.AwayFromZero)
            : 0m;

        return new CollectedTaxLine
        {
            SortOrder = 0,
            Code = $"{country}_tax".ToLowerInvariant(),
            Name = "TAX",
            Rate = rate,
            Amount = record.TaxAmountTotal,
            Type = country is "CA" ? "combined" : "standard"
        };
    }

    private static string ResolveTransactionReference(string? paymentIntentId, string sessionId) =>
        string.IsNullOrWhiteSpace(paymentIntentId) ? sessionId : paymentIntentId;

    private async Task<Guid> ResolveClientApplicationIdAsync(
        PaymentTransaction? payment,
        Invoice stripeInvoice,
        CancellationToken cancellationToken)
    {
        if (payment is not null)
        {
            return payment.ClientApplicationId;
        }

        if (stripeInvoice.Metadata.TryGetValue("payment_code", out var paymentCode))
        {
            var linkedPayment = await _db.PaymentTransactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode, cancellationToken);
            if (linkedPayment is not null)
            {
                return linkedPayment.ClientApplicationId;
            }
        }

        throw new InvalidOperationException(
            $"Impossible de résoudre l'application cliente pour la facture Stripe {stripeInvoice.Id}.");
    }

    private static string? GetPaymentIntentIdFromInvoice(Invoice stripeInvoice)
    {
        var payment = stripeInvoice.Payments?.Data?.FirstOrDefault();
        if (payment?.Payment?.PaymentIntentId is { } paymentIntentId)
        {
            return paymentIntentId;
        }

        if (payment?.Payment?.PaymentIntent is { Id: var expandedId })
        {
            return expandedId;
        }

        return null;
    }
}
