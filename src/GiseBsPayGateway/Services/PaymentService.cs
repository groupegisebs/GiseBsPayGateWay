using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Services;

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeService _stripeService;
    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly IAuditService _auditService;

    public PaymentService(
        ApplicationDbContext db,
        IStripeService stripeService,
        IStripeSettingsProvider stripeSettings,
        IAuditService auditService)
    {
        _db = db;
        _stripeService = stripeService;
        _stripeSettings = stripeSettings;
        _auditService = auditService;
    }

    public async Task<DTOs.CheckoutSessionResponse> CreateCheckoutSessionAsync(ClientApplication app, DTOs.CreateCheckoutSessionRequest request, CancellationToken cancellationToken = default)
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

        var (sessionId, url, clientSecret) = await _stripeService.CreateCheckoutSessionAsync(
            payment, customer, plan, request.SuccessUrl, request.CancelUrl, request.TrialDays, request.Embedded, cancellationToken);

        payment.StripeCheckoutSessionId = sessionId;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync("CheckoutSessionCreated", nameof(PaymentTransaction), payment.Id.ToString(), true,
            $"PaymentCode={paymentCode};Embedded={request.Embedded}", app.AppCode);

        var stripeSettings = await _stripeSettings.GetActiveAsync(cancellationToken);
        return new DTOs.CheckoutSessionResponse(
            paymentCode,
            url ?? string.Empty,
            sessionId,
            payment.Status.ToString(),
            clientSecret,
            request.Embedded ? stripeSettings?.PublishableKey : null);
    }

    public async Task<DTOs.PaymentResponse?> GetPaymentByCodeAsync(ClientApplication app, string paymentCode, CancellationToken cancellationToken = default)
    {
        var payment = await _db.PaymentTransactions.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan)
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.PaymentCode == paymentCode, cancellationToken);

        return payment is null ? null : MapPayment(payment);
    }

    public async Task<IReadOnlyList<DTOs.SubscriptionResponse>> GetCustomerSubscriptionsAsync(ClientApplication app, string customerCode, CancellationToken cancellationToken = default)
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

    public async Task<DTOs.CancelSubscriptionResponse> CancelSubscriptionAsync(ClientApplication app, DTOs.CancelSubscriptionRequest request, CancellationToken cancellationToken = default)
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

        return new DTOs.CancelSubscriptionResponse(
            subscription.SubscriptionCode,
            subscription.Status.ToString(),
            subscription.CancelledAt);
    }

    private static DTOs.PaymentResponse MapPayment(PaymentTransaction payment) =>
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
            payment.FailureReason);

    private static DTOs.SubscriptionResponse MapSubscription(Subscription subscription) =>
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
