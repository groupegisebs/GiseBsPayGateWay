using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Stripe.Checkout;

namespace GiseBsPayGateway.Tests.Services;

public class PendingPaymentExpiryServiceTests
{
    [Fact]
    public async Task ExpireAbandoned_SansSession_MarqueCancelled()
    {
        await using var db = TestDbContextFactory.Create(nameof(ExpireAbandoned_SansSession_MarqueCancelled));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var customer = await SeedCustomerAsync(db, app);

        var payment = await SeedPendingPaymentAsync(db, app, customer, product, plan, hoursAgo: 25);

        var sut = CreateService(db, stripe: null, webhook: null);

        var result = await sut.ExpireAbandonedAsync();

        Assert.Equal(1, result.Cancelled);
        Assert.Equal(0, result.Reconciled);
        var updated = await db.PaymentTransactions.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Cancelled, updated!.Status);
        Assert.Contains("Checkout non démarré", updated.FailureReason);
    }

    [Fact]
    public async Task ExpireAbandoned_RecentPending_NeChangeRien()
    {
        await using var db = TestDbContextFactory.Create(nameof(ExpireAbandoned_RecentPending_NeChangeRien));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var customer = await SeedCustomerAsync(db, app);

        await SeedPendingPaymentAsync(db, app, customer, product, plan, hoursAgo: 2);

        var sut = CreateService(db, stripe: null, webhook: null);

        var result = await sut.ExpireAbandonedAsync();

        Assert.Equal(0, result.Cancelled);
        Assert.Equal(0, result.Reconciled);
        Assert.All(db.PaymentTransactions, p => Assert.Equal(PaymentStatus.Pending, p.Status));
    }

    [Fact]
    public async Task ExpireAbandoned_SessionPayee_ReconcilieAuLieuAnnuler()
    {
        await using var db = TestDbContextFactory.Create(nameof(ExpireAbandoned_SessionPayee_ReconcilieAuLieuAnnuler));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var customer = await SeedCustomerAsync(db, app);

        var payment = await SeedPendingPaymentAsync(
            db, app, customer, product, plan, hoursAgo: 30, stripeSessionId: "cs_test_paid");

        var session = new Session { Id = "cs_test_paid", PaymentStatus = "paid", Status = "complete" };

        var stripe = new Mock<IStripePaymentDetailsService>();
        stripe.Setup(s => s.GetCheckoutSessionAsync("cs_test_paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var webhook = new Mock<IWebhookService>();
        webhook.Setup(w => w.TryCompleteFromCheckoutSessionAsync(
                It.IsAny<PaymentTransaction>(), session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateService(db, stripe.Object, webhook.Object);

        var result = await sut.ExpireAbandonedAsync();

        Assert.Equal(0, result.Cancelled);
        Assert.Equal(1, result.Reconciled);
        var updated = await db.PaymentTransactions.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Pending, updated!.Status);
    }

    [Fact]
    public async Task ExpireAbandoned_SessionExpireeNonPayee_MarqueCancelled()
    {
        await using var db = TestDbContextFactory.Create(nameof(ExpireAbandoned_SessionExpireeNonPayee_MarqueCancelled));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var customer = await SeedCustomerAsync(db, app);

        var payment = await SeedPendingPaymentAsync(
            db, app, customer, product, plan, hoursAgo: 30, stripeSessionId: "cs_test_expired");

        var session = new Session { Id = "cs_test_expired", PaymentStatus = "unpaid", Status = "expired" };

        var stripe = new Mock<IStripePaymentDetailsService>();
        stripe.Setup(s => s.GetCheckoutSessionAsync("cs_test_expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var webhook = new Mock<IWebhookService>();
        webhook.Setup(w => w.TryCompleteFromCheckoutSessionAsync(
                It.IsAny<PaymentTransaction>(), session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService(db, stripe.Object, webhook.Object);

        var result = await sut.ExpireAbandonedAsync();

        Assert.Equal(1, result.Cancelled);
        var updated = await db.PaymentTransactions.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Cancelled, updated!.Status);
        Assert.Contains("expirée", updated.FailureReason);
    }

    private static PendingPaymentExpiryService CreateService(
        ApplicationDbContext db,
        IStripePaymentDetailsService? stripe,
        IWebhookService? webhook)
    {
        stripe ??= Mock.Of<IStripePaymentDetailsService>();
        webhook ??= Mock.Of<IWebhookService>();
        var audit = Mock.Of<IAuditService>();
        var options = Microsoft.Extensions.Options.Options.Create(new PendingPaymentExpiryOptions
        {
            Enabled = true,
            ExpiryHours = 24,
            BatchSize = 100
        });
        var logger = Mock.Of<ILogger<PendingPaymentExpiryService>>();

        return new PendingPaymentExpiryService(db, stripe, webhook, audit, options, logger);
    }

    private static async Task<Customer> SeedCustomerAsync(ApplicationDbContext db, ClientApplication app)
    {
        var customer = new Customer
        {
            ClientApplicationId = app.Id,
            CustomerCode = "BG-test",
            Email = "test@example.com"
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<PaymentTransaction> SeedPendingPaymentAsync(
        ApplicationDbContext db,
        ClientApplication app,
        Customer customer,
        Product product,
        PricingPlan plan,
        int hoursAgo,
        string? stripeSessionId = null)
    {
        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = $"PAY-{app.AppCode}-{Guid.NewGuid():N}"[..32],
            Status = PaymentStatus.Pending,
            Amount = plan.Amount,
            Currency = plan.Currency,
            StripeCheckoutSessionId = stripeSessionId,
            CreatedAt = DateTime.UtcNow.AddHours(-hoursAgo),
            UpdatedAt = DateTime.UtcNow.AddHours(-hoursAgo)
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }
}
