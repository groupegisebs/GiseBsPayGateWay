using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using SubscriptionEntity = GiseBsPayGateway.Entities.Subscription;

namespace GiseBsPayGateway.Services;

public interface IInvoiceService
{
    Task SaveFromCheckoutCompletedAsync(Session session, PaymentTransaction payment, CancellationToken cancellationToken = default);
    Task SaveFromStripeInvoiceAsync(Invoice stripeInvoice, InvoiceStatus status, CancellationToken cancellationToken = default);
    Task<PaymentInvoice?> GetByPaymentCodeAsync(Guid clientApplicationId, string paymentCode, CancellationToken cancellationToken = default);
    Task<PaymentInvoice?> EnsureInvoiceForPaymentAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
    Task<(byte[] Content, string FileName)?> GetPdfAsync(PaymentInvoice invoice, CancellationToken cancellationToken = default);
}

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _db;
    private readonly IGisebsInvoiceCodeGenerator _invoiceCodeGenerator;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IInvoiceFileStorage _fileStorage;
    private readonly IStripePaymentDetailsService _stripePaymentDetailsService;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        ApplicationDbContext db,
        IGisebsInvoiceCodeGenerator invoiceCodeGenerator,
        IInvoicePdfGenerator pdfGenerator,
        IInvoiceFileStorage fileStorage,
        IStripePaymentDetailsService stripePaymentDetailsService,
        ILogger<InvoiceService> logger)
    {
        _db = db;
        _invoiceCodeGenerator = invoiceCodeGenerator;
        _pdfGenerator = pdfGenerator;
        _fileStorage = fileStorage;
        _stripePaymentDetailsService = stripePaymentDetailsService;
        _logger = logger;
    }

    public async Task SaveFromCheckoutCompletedAsync(Session session, PaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        if (payment.PricingPlan.BillingInterval != BillingInterval.OneTime)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.InvoiceId))
        {
            return;
        }

        if (await _db.PaymentInvoices.AnyAsync(
                x => x.PaymentTransactionId == payment.Id ||
                     (session.PaymentIntentId != null && x.StripePaymentIntentId == session.PaymentIntentId),
                cancellationToken))
        {
            return;
        }

        var receiptUrl = await TryGetReceiptUrlAsync(session.PaymentIntentId, cancellationToken);
        var invoice = await BuildInvoiceFromPaymentAsync(payment, cancellationToken);
        invoice.StripeCheckoutSessionId = session.Id;
        invoice.StripePaymentIntentId = session.PaymentIntentId;
        invoice.ReceiptUrl = receiptUrl;
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = payment.PaidAt ?? DateTime.UtcNow;
        invoice.InvoiceDate = invoice.PaidAt.Value;
        StripeCheckoutFinancials.CopyPaymentFinancialsToInvoice(invoice, payment);

        _db.PaymentInvoices.Add(invoice);
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await GenerateAndStorePdfAsync(invoice, payment, cancellationToken);

        _logger.LogInformation("Facture {InvoiceCode} créée pour paiement {PaymentCode}", invoice.InvoiceCode, payment.PaymentCode);
    }

    public async Task SaveFromStripeInvoiceAsync(Invoice stripeInvoice, InvoiceStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stripeInvoice.Id))
        {
            return;
        }

        var existing = await _db.PaymentInvoices
            .Include(x => x.PaymentTransaction)
            .FirstOrDefaultAsync(x => x.StripeInvoiceId == stripeInvoice.Id, cancellationToken);

        if (existing is not null)
        {
            UpdateFromStripeInvoice(existing, stripeInvoice, status);
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await GenerateAndStorePdfAsync(existing, existing.PaymentTransaction, cancellationToken);
            return;
        }

        var context = await ResolveInvoiceContextAsync(stripeInvoice, cancellationToken);
        if (context is null)
        {
            _logger.LogWarning("Facture Stripe {InvoiceId} ignorée : contexte local introuvable", stripeInvoice.Id);
            return;
        }

        var invoice = await BuildInvoiceFromContextAsync(context, cancellationToken);
        UpdateFromStripeInvoice(invoice, stripeInvoice, status);
        invoice.PaymentTransactionId = context.Payment?.Id;
        invoice.SubscriptionId = context.Subscription?.Id;

        if (context.Payment is not null)
        {
            context.Payment.StripeInvoiceId = stripeInvoice.Id;
            context.Payment.UpdatedAt = DateTime.UtcNow;
        }

        _db.PaymentInvoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);
        await GenerateAndStorePdfAsync(invoice, context.Payment, cancellationToken);

        _logger.LogInformation("Facture Stripe {StripeInvoiceId} enregistrée ({InvoiceCode})", stripeInvoice.Id, invoice.InvoiceCode);
    }

    public async Task<PaymentInvoice?> GetByPaymentCodeAsync(Guid clientApplicationId, string paymentCode, CancellationToken cancellationToken = default)
    {
        return await _db.PaymentInvoices.AsNoTracking()
            .Include(x => x.PaymentTransaction)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == clientApplicationId &&
                     x.PaymentTransaction != null &&
                     x.PaymentTransaction.PaymentCode == paymentCode,
                cancellationToken);
    }

    public async Task<PaymentInvoice?> EnsureInvoiceForPaymentAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        if (payment.Status != PaymentStatus.Succeeded)
        {
            return null;
        }

        await EnsurePaymentGraphLoadedAsync(payment, cancellationToken);

        var existing = await _db.PaymentInvoices
            .Include(x => x.PaymentTransaction)
            .FirstOrDefaultAsync(x => x.PaymentTransactionId == payment.Id, cancellationToken);

        if (existing is not null)
        {
            await BackfillPaymentFinancialsAsync(payment, cancellationToken);
            StripeCheckoutFinancials.CopyPaymentFinancialsToInvoice(existing, payment);
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await GenerateAndStorePdfAsync(existing, payment, cancellationToken);
            return existing;
        }

        await BackfillPaymentFinancialsAsync(payment, cancellationToken);

        var invoice = await BuildInvoiceFromPaymentAsync(payment, cancellationToken);
        invoice.StripeCheckoutSessionId ??= payment.StripeCheckoutSessionId;
        invoice.StripePaymentIntentId ??= payment.StripePaymentIntentId;
        invoice.StripeInvoiceId ??= payment.StripeInvoiceId;
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = payment.PaidAt ?? DateTime.UtcNow;
        invoice.InvoiceDate = invoice.PaidAt.Value;
        StripeCheckoutFinancials.CopyPaymentFinancialsToInvoice(invoice, payment);

        _db.PaymentInvoices.Add(invoice);
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await GenerateAndStorePdfAsync(invoice, payment, cancellationToken);

        _logger.LogInformation("Facture {InvoiceCode} générée rétroactivement pour {PaymentCode}", invoice.InvoiceCode, payment.PaymentCode);
        return invoice;
    }

    public Task<(byte[] Content, string FileName)?> GetPdfAsync(PaymentInvoice invoice, CancellationToken cancellationToken = default)
    {
        var fullPath = _fileStorage.ResolveFullPath(invoice.StoredPdfRelativePath);
        if (fullPath is null)
        {
            return Task.FromResult<(byte[] Content, string FileName)?>(null);
        }

        var content = System.IO.File.ReadAllBytes(fullPath);
        return Task.FromResult<(byte[] Content, string FileName)?>((content, $"{invoice.InvoiceCode}.pdf"));
    }

    private async Task EnsurePaymentGraphLoadedAsync(PaymentTransaction payment, CancellationToken cancellationToken)
    {
        await _db.Entry(payment).Reference(x => x.Customer).LoadAsync(cancellationToken);
        await _db.Entry(payment).Reference(x => x.Product).LoadAsync(cancellationToken);
        await _db.Entry(payment).Reference(x => x.PricingPlan).LoadAsync(cancellationToken);
    }

    private async Task GenerateAndStorePdfAsync(
        PaymentInvoice invoice,
        PaymentTransaction? payment,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(invoice.StoredPdfRelativePath) &&
            _fileStorage.ResolveFullPath(invoice.StoredPdfRelativePath) is not null)
        {
            return;
        }

        var pdfBytes = _pdfGenerator.Generate(invoice, payment ?? invoice.PaymentTransaction);
        invoice.StoredPdfRelativePath = await _fileStorage.SavePdfAsync(invoice.InvoiceCode, pdfBytes, cancellationToken);
        invoice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<InvoiceContext?> ResolveInvoiceContextAsync(Invoice stripeInvoice, CancellationToken cancellationToken)
    {
        PaymentTransaction? payment = null;
        SubscriptionEntity? subscription = null;

        if (stripeInvoice.Metadata.TryGetValue("payment_code", out var paymentCode))
        {
            payment = await _db.PaymentTransactions
                .Include(x => x.Customer)
                .Include(x => x.Product)
                .Include(x => x.PricingPlan)
                .Include(x => x.ClientApplication)
                .FirstOrDefaultAsync(x => x.PaymentCode == paymentCode, cancellationToken);
        }

        var subscriptionId = GetInvoiceSubscriptionId(stripeInvoice);
        if (subscriptionId is not null)
        {
            subscription = await _db.Subscriptions
                .Include(x => x.Customer)
                .Include(x => x.Product)
                .Include(x => x.PricingPlan)
                .Include(x => x.ClientApplication)
                .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscriptionId, cancellationToken);
        }

        if (payment is not null)
        {
            return new InvoiceContext(payment.ClientApplication, payment.Customer, payment.Product, payment.PricingPlan, payment, subscription ?? payment.Subscription);
        }

        if (subscription is not null)
        {
            payment = await _db.PaymentTransactions
                .Where(x => x.SubscriptionId == subscription.Id)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return new InvoiceContext(subscription.ClientApplication, subscription.Customer, subscription.Product, subscription.PricingPlan, payment, subscription);
        }

        return null;
    }

    private async Task<PaymentInvoice> BuildInvoiceFromPaymentAsync(PaymentTransaction payment, CancellationToken cancellationToken)
    {
        return new PaymentInvoice
        {
            InvoiceCode = await _invoiceCodeGenerator.GenerateUniqueAsync(cancellationToken),
            ClientApplicationId = payment.ClientApplicationId,
            CustomerId = payment.CustomerId,
            ProductId = payment.ProductId,
            PricingPlanId = payment.PricingPlanId,
            PaymentTransactionId = payment.Id,
            SubscriptionId = payment.SubscriptionId,
            CustomerCode = payment.Customer.CustomerCode,
            CustomerEmail = payment.Customer.Email,
            CustomerName = payment.Customer.FullName,
            ProductCode = payment.Product.ProductCode,
            ProductName = payment.Product.Name,
            PlanCode = payment.PricingPlan.PlanCode,
            PlanName = payment.PricingPlan.Name,
            Amount = StripeCheckoutFinancials.ResolveCustomerTotal(payment),
            Currency = payment.Currency,
            AmountSubtotal = payment.AmountSubtotal ?? payment.Amount,
            LineItemsDescription = $"{payment.Product.Name} — {payment.PricingPlan.Name}"
        };
    }

    private async Task BackfillPaymentFinancialsAsync(PaymentTransaction payment, CancellationToken cancellationToken)
    {
        var updated = false;

        if ((!payment.TaxAmount.HasValue || !payment.AmountSubtotal.HasValue || !payment.GrossAmount.HasValue) &&
            !string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
        {
            var session = await _stripePaymentDetailsService.GetCheckoutSessionAsync(
                payment.StripeCheckoutSessionId,
                cancellationToken);
            if (session is not null)
            {
                StripeCheckoutFinancials.ApplySessionTaxToPayment(payment, session);
                updated = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(payment.StripePaymentIntentId) && !payment.StripeFee.HasValue)
        {
            var details = await _stripePaymentDetailsService.GetBalanceTransactionDetailsAsync(
                payment.StripePaymentIntentId,
                cancellationToken);
            StripeCheckoutFinancials.ApplyBalanceTransactionToPayment(payment, details);
            updated = true;
        }

        if (updated)
        {
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<PaymentInvoice> BuildInvoiceFromContextAsync(InvoiceContext context, CancellationToken cancellationToken)
    {
        var amount = context.Payment?.Amount ?? context.Plan.Amount;
        var currency = context.Payment?.Currency ?? context.Plan.Currency;

        return new PaymentInvoice
        {
            InvoiceCode = await _invoiceCodeGenerator.GenerateUniqueAsync(cancellationToken),
            ClientApplicationId = context.App.Id,
            CustomerId = context.Customer.Id,
            ProductId = context.Product.Id,
            PricingPlanId = context.Plan.Id,
            CustomerCode = context.Customer.CustomerCode,
            CustomerEmail = context.Customer.Email,
            CustomerName = context.Customer.FullName,
            ProductCode = context.Product.ProductCode,
            ProductName = context.Product.Name,
            PlanCode = context.Plan.PlanCode,
            PlanName = context.Plan.Name,
            Amount = amount,
            Currency = currency,
            LineItemsDescription = $"{context.Product.Name} — {context.Plan.Name}"
        };
    }

    private static void UpdateFromStripeInvoice(PaymentInvoice invoice, Invoice stripeInvoice, InvoiceStatus status)
    {
        invoice.StripeInvoiceId = stripeInvoice.Id;
        invoice.StripeInvoiceNumber = stripeInvoice.Number;
        invoice.StripePaymentIntentId ??= GetPaymentIntentIdFromInvoice(stripeInvoice);
        invoice.HostedInvoiceUrl = stripeInvoice.HostedInvoiceUrl;
        invoice.InvoicePdfUrl = stripeInvoice.InvoicePdf;
        invoice.BillingReason = stripeInvoice.BillingReason;
        invoice.Status = status;
        invoice.InvoiceDate = stripeInvoice.Created;
        invoice.PeriodStart = stripeInvoice.PeriodStart;
        invoice.PeriodEnd = stripeInvoice.PeriodEnd;
        invoice.LineItemsDescription = BuildLineItemsDescription(stripeInvoice) ?? invoice.LineItemsDescription;

        if (!string.IsNullOrWhiteSpace(stripeInvoice.CustomerEmail))
        {
            invoice.CustomerEmail = stripeInvoice.CustomerEmail;
        }

        if (stripeInvoice.AmountPaid > 0)
        {
            invoice.Amount = stripeInvoice.AmountPaid / 100m;
        }
        else if (stripeInvoice.Total > 0)
        {
            invoice.Amount = stripeInvoice.Total / 100m;
        }

        if (!string.IsNullOrWhiteSpace(stripeInvoice.Currency))
        {
            invoice.Currency = stripeInvoice.Currency.ToLowerInvariant();
        }

        if (status == InvoiceStatus.Paid)
        {
            invoice.PaidAt = stripeInvoice.StatusTransitions?.PaidAt ?? DateTime.UtcNow;
        }
    }

    private async Task<string?> TryGetReceiptUrlAsync(string? paymentIntentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return null;
        }

        try
        {
            var service = new PaymentIntentService();
            var intent = await service.GetAsync(paymentIntentId, cancellationToken: cancellationToken);
            return intent.LatestCharge?.ReceiptUrl;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de récupérer le reçu pour {PaymentIntentId}", paymentIntentId);
            return null;
        }
    }

    private static string? BuildLineItemsDescription(Invoice stripeInvoice)
    {
        var lines = stripeInvoice.Lines?.Data;
        if (lines is null || lines.Count == 0)
        {
            return null;
        }

        return string.Join(" | ", lines.Select(line =>
        {
            var label = line.Description ?? "Article";
            var qty = line.Quantity ?? 1;
            return qty > 1 ? $"{label} x{qty}" : label;
        }));
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

    private static string? GetInvoiceSubscriptionId(Invoice? invoice) =>
        invoice?.Parent?.SubscriptionDetails?.SubscriptionId
        ?? invoice?.Parent?.SubscriptionDetails?.Subscription?.Id;

    private sealed record InvoiceContext(
        ClientApplication App,
        Entities.Customer Customer,
        Entities.Product Product,
        PricingPlan Plan,
        PaymentTransaction? Payment,
        SubscriptionEntity? Subscription);
}
