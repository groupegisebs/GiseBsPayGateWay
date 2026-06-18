using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Services;

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeService _stripeService;
    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly IAuditService _auditService;
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoiceLinkBuilder _invoiceLinkBuilder;
    private readonly ICollectedTaxService _collectedTaxService;

    public PaymentService(
        ApplicationDbContext db,
        IStripeService stripeService,
        IStripeSettingsProvider stripeSettings,
        IAuditService auditService,
        IInvoiceService invoiceService,
        IInvoiceLinkBuilder invoiceLinkBuilder,
        ICollectedTaxService collectedTaxService)
    {
        _db = db;
        _stripeService = stripeService;
        _stripeSettings = stripeSettings;
        _auditService = auditService;
        _invoiceService = invoiceService;
        _invoiceLinkBuilder = invoiceLinkBuilder;
        _collectedTaxService = collectedTaxService;
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(ClientApplication app, CreateCheckoutSessionRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(x => x.PricingPlans)
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.ProductCode == request.ProductCode && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"Produit '{request.ProductCode}' introuvable.");

        var plan = product.PricingPlans.FirstOrDefault(x => x.PlanCode == request.PlanCode && x.IsActive)
            ?? throw new InvalidOperationException($"Plan '{request.PlanCode}' introuvable.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.CustomerCode == request.CustomerCode, cancellationToken);

        if (customer is null)
        {
            customer = new Customer
            {
                ClientApplicationId = app.Id,
                CustomerCode = request.CustomerCode,
                Email = request.Email,
                FullName = request.FullName,
                ExternalUserId = request.ExternalUserId
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            customer.Email = request.Email;
            customer.FullName = request.FullName ?? customer.FullName;
            customer.ExternalUserId = request.ExternalUserId ?? customer.ExternalUserId;
            customer.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var paymentCode = $"PAY-{app.AppCode.ToUpperInvariant()}-{Guid.NewGuid():N}"[..32];
        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = paymentCode,
            Status = PaymentStatus.Pending,
            Amount = plan.Amount,
            Currency = plan.Currency,
            Product = product,
            PricingPlan = plan,
            Customer = customer
        };

        _db.PaymentTransactions.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        BillingAddressDto? sessionBillingAddress = request.BillingAddress;
        if (sessionBillingAddress is { } billingAddress)
        {
            var formattedBilling = StripeAddressFormatter.Format(billingAddress);
            payment.BillingCountry = formattedBilling.Country;
            payment.BillingState = formattedBilling.State;
            sessionBillingAddress = formattedBilling;
        }

        var (sessionId, url, clientSecret) = await _stripeService.CreateCheckoutSessionAsync(
            payment,
            customer,
            plan,
            request.SuccessUrl,
            request.CancelUrl,
            request.TrialDays,
            request.Embedded,
            sessionBillingAddress,
            request.CustomerUpdate,
            cancellationToken);

        payment.StripeCheckoutSessionId = sessionId;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync("CheckoutSessionCreated", nameof(PaymentTransaction), payment.Id.ToString(), true,
            $"PaymentCode={paymentCode};Embedded={request.Embedded}", app.AppCode);

        var stripeSettings = await _stripeSettings.GetActiveAsync(cancellationToken);
        return new CheckoutSessionResponse(
            paymentCode,
            url ?? string.Empty,
            sessionId,
            payment.Status.ToString(),
            clientSecret,
            request.Embedded ? stripeSettings?.PublishableKey : null);
    }

    public async Task<PaymentResponse?> GetPaymentByCodeAsync(ClientApplication app, string paymentCode, CancellationToken cancellationToken = default)
    {
        var payment = await _db.PaymentTransactions
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan)
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.PaymentCode == paymentCode, cancellationToken);

        if (payment is null)
        {
            return null;
        }

        var invoice = payment.Status == PaymentStatus.Succeeded
            ? await _invoiceService.EnsureInvoiceForPaymentAsync(payment, cancellationToken)
            : await _invoiceService.GetByPaymentCodeAsync(app.Id, paymentCode, cancellationToken);

        var collectedTax = payment.Status == PaymentStatus.Succeeded
            ? await _collectedTaxService.GetByPaymentCodeAsync(app.Id, paymentCode, cancellationToken)
            : null;

        return MapPayment(payment, invoice, collectedTax);
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetCustomerSubscriptionsAsync(ClientApplication app, string customerCode, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan)
            .Where(x => x.ClientApplicationId == app.Id && x.Customer.CustomerCode == customerCode)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return subscriptions.Select(MapSubscription).ToList();
    }

    public async Task<CancelSubscriptionResponse> CancelSubscriptionAsync(ClientApplication app, CancelSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.SubscriptionCode == request.SubscriptionCode, cancellationToken)
            ?? throw new InvalidOperationException($"Abonnement '{request.SubscriptionCode}' introuvable.");

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            throw new InvalidOperationException("Abonnement Stripe non lié.");
        }

        await _stripeService.CancelSubscriptionAsync(subscription.StripeSubscriptionId, request.CancelImmediately, cancellationToken);

        subscription.CancelAtPeriodEnd = !request.CancelImmediately;
        if (request.CancelImmediately)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAt = DateTime.UtcNow;
        }

        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync("SubscriptionCancelled", nameof(Subscription), subscription.Id.ToString(), true,
            $"Code={subscription.SubscriptionCode}", app.AppCode);

        return new CancelSubscriptionResponse(
            subscription.SubscriptionCode,
            subscription.Status.ToString(),
            subscription.CancelledAt);
    }

    private PaymentResponse MapPayment(
        PaymentTransaction payment,
        PaymentInvoice? invoice,
        CollectedTaxRecord? collectedTax) =>
        new(
            payment.PaymentCode,
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency,
            payment.Customer.CustomerCode,
            payment.Product.ProductCode,
            payment.PricingPlan.PlanCode,
            payment.CreatedAt,
            payment.PaidAt,
            payment.FailureReason,
            payment.StripeCheckoutSessionId,
            payment.StripePaymentIntentId,
            invoice?.InvoiceCode,
            invoice is null ? null : _invoiceLinkBuilder.BuildDownloadUrl(invoice.InvoiceCode),
            payment.AmountSubtotal,
            payment.TaxAmount,
            payment.GrossAmount,
            payment.StripeFee,
            payment.NetAmount,
            payment.StripeBalanceTransactionId,
            collectedTax?.BillingCountry ?? payment.BillingCountry,
            collectedTax?.BillingState ?? payment.BillingState,
            collectedTax is not null ? CollectedTaxMapper.ToBillingAddressDto(collectedTax) : null,
            collectedTax is not null ? CollectedTaxMapper.ToLineDtos(collectedTax.Lines) : null);

    private static SubscriptionResponse MapSubscription(Subscription subscription) =>
        new(
            subscription.SubscriptionCode,
            subscription.Status.ToString(),
            subscription.Customer.CustomerCode,
            subscription.Product.ProductCode,
            subscription.PricingPlan.PlanCode,
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd);
}
