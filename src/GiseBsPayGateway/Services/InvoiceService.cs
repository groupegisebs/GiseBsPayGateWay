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
}

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(ApplicationDbContext db, ILogger<InvoiceService> logger)
    {
        _db = db;
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

        var invoice = BuildInvoiceFromPayment(payment);
        invoice.StripeCheckoutSessionId = session.Id;
        invoice.StripePaymentIntentId = session.PaymentIntentId;
        invoice.ReceiptUrl = receiptUrl;
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = payment.PaidAt ?? DateTime.UtcNow;
        invoice.InvoiceDate = invoice.PaidAt.Value;

        _db.PaymentInvoices.Add(invoice);
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Facture {InvoiceCode} créée pour paiement {PaymentCode}", invoice.InvoiceCode, payment.PaymentCode);
    }

    public async Task SaveFromStripeInvoiceAsync(Invoice stripeInvoice, InvoiceStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stripeInvoice.Id))
        {
            return;
        }

        var existing = await _db.PaymentInvoices
            .FirstOrDefaultAsync(x => x.StripeInvoiceId == stripeInvoice.Id, cancellationToken);

        if (existing is not null)
        {
            UpdateFromStripeInvoice(existing, stripeInvoice, status);
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var context = await ResolveInvoiceContextAsync(stripeInvoice, cancellationToken);
        if (context is null)
        {
            _logger.LogWarning("Facture Stripe {InvoiceId} ignorée : contexte local introuvable", stripeInvoice.Id);
            return;
        }

        var invoice = BuildInvoiceFromContext(context);
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

        _logger.LogInformation("Facture Stripe {StripeInvoiceId} enregistrée ({InvoiceCode})", stripeInvoice.Id, invoice.InvoiceCode);
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

    private static PaymentInvoice BuildInvoiceFromPayment(PaymentTransaction payment)
    {
        return new PaymentInvoice
        {
            InvoiceCode = $"INV-{payment.PaymentCode}",
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
            Amount = payment.Amount,
            Currency = payment.Currency,
            LineItemsDescription = $"{payment.Product.Name} — {payment.PricingPlan.Name}"
        };
    }

    private static PaymentInvoice BuildInvoiceFromContext(InvoiceContext context)
    {
        var amount = context.Payment?.Amount ?? context.Plan.Amount;
        var currency = context.Payment?.Currency ?? context.Plan.Currency;

        return new PaymentInvoice
        {
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

        invoice.InvoiceCode = !string.IsNullOrWhiteSpace(stripeInvoice.Number)
            ? stripeInvoice.Number
            : $"INV-{stripeInvoice.Id[^12..]}";

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
