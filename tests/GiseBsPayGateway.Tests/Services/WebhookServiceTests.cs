using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe.Checkout;
using StripeSubscription = Stripe.Subscription;

namespace GiseBsPayGateway.Tests.Services;

public class WebhookServiceTests
{
    [Fact]
    public async Task TryCompleteFromCheckoutSession_AbonnementPayeSansPaymentIntent_MarqueSucceeded()
    {
        await using var db = TestDbContextFactory.Create(nameof(TryCompleteFromCheckoutSession_AbonnementPayeSansPaymentIntent_MarqueSucceeded));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(
            db, app, "VENDOR-CREATOR-PLAN", "MONTHLY", 5m);
        var customer = await SeedCustomerAsync(db, app);
        var payment = await SeedPendingPaymentAsync(
            db, app, customer, product, plan, "PAY-BOUTIQUEGISE-39154529412543b", "cs_test_sub");

        var session = new Session
        {
            Id = "cs_test_sub",
            PaymentStatus = "paid",
            Status = "complete",
            SubscriptionId = "sub_test_active"
        };

        var stripe = new Mock<IStripePaymentDetailsService>();
        stripe.Setup(s => s.GetCheckoutSessionAsync("cs_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        stripe.Setup(s => s.GetSubscriptionPaymentIntentIdAsync("sub_test_active", It.IsAny<CancellationToken>()))
            .ReturnsAsync("pi_test_sub");

        var sut = CreateService(db, stripe.Object);

        var loadedPayment = await db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.ClientApplication)
            .FirstAsync(x => x.Id == payment.Id);

        var result = await sut.TryCompleteFromCheckoutSessionAsync(loadedPayment, session);

        Assert.True(result);
        var updated = await db.PaymentTransactions.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, updated!.Status);
        Assert.NotNull(updated.PaidAt);
        Assert.Equal("pi_test_sub", updated.StripePaymentIntentId);
        Assert.Equal("sub_test_active", updated.Subscription?.StripeSubscriptionId);
    }

    [Fact]
    public async Task HandleSubscriptionCreated_AvecPaymentCode_MarquePaiementPendingSucceeded()
    {
        await using var db = TestDbContextFactory.Create(nameof(HandleSubscriptionCreated_AvecPaymentCode_MarquePaiementPendingSucceeded));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(
            db, app, "VENDOR-CREATOR-PLAN", "MONTHLY", 5m);
        var customer = await SeedCustomerAsync(db, app);
        var paymentCode = "PAY-BOUTIQUEGISE-39154529412543b";
        var payment = await SeedPendingPaymentAsync(
            db, app, customer, product, plan, paymentCode, "cs_test_sub");

        var stripeSubscription = new StripeSubscription
        {
            Id = "sub_test_active",
            Status = "active",
            Metadata = new Dictionary<string, string> { ["payment_code"] = paymentCode }
        };

        var stripe = new Mock<IStripePaymentDetailsService>();
        stripe.Setup(s => s.GetCheckoutSessionAsync("cs_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);
        stripe.Setup(s => s.GetSubscriptionPaymentIntentIdAsync("sub_test_active", It.IsAny<CancellationToken>()))
            .ReturnsAsync("pi_test_sub");

        var sut = CreateService(db, stripe.Object);
        await InvokeTryFinalizeFromStripeSubscriptionAsync(sut, stripeSubscription);

        var updated = await db.PaymentTransactions.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, updated!.Status);
        Assert.NotNull(updated.PaidAt);
        Assert.Equal("sub_test_active", updated.Subscription?.StripeSubscriptionId);
    }

    [Fact]
    public async Task TryCompleteFromCheckoutSession_SucceededSansDonnees_RemplitPaymentIntentEtFrais()
    {
        await using var db = TestDbContextFactory.Create(nameof(TryCompleteFromCheckoutSession_SucceededSansDonnees_RemplitPaymentIntentEtFrais));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(
            db, app, "VENDOR-CREATOR-PLAN", "MONTHLY", 5m);
        var customer = await SeedCustomerAsync(db, app);
        var payment = await SeedPendingPaymentAsync(
            db, app, customer, product, plan, "PAY-BOUTIQUEGISE-39154529412543b", "cs_test_sub");
        payment.Status = PaymentStatus.Succeeded;
        payment.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var session = new Session
        {
            Id = "cs_test_sub",
            PaymentStatus = "paid",
            Status = "complete",
            SubscriptionId = "sub_test_active"
        };

        var stripe = new Mock<IStripePaymentDetailsService>();
        stripe.Setup(s => s.GetCheckoutSessionAsync("cs_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        stripe.Setup(s => s.GetSubscriptionPaymentIntentIdAsync("sub_test_active", It.IsAny<CancellationToken>()))
            .ReturnsAsync("pi_test_sub");
        stripe.Setup(s => s.GetBalanceTransactionDetailsAsync("pi_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeBalanceTransactionDetails(0.45m, 4.55m, 5m, "txn_test_sub"));

        var invoiceService = new Mock<IInvoiceService>();
        invoiceService.Setup(s => s.EnrichSuccessfulPaymentFinancialsAsync(
                It.IsAny<PaymentTransaction>(),
                It.IsAny<Session?>(),
                It.IsAny<Stripe.Invoice?>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, Session?, Stripe.Invoice?, CancellationToken>((p, checkoutSession, _, _) =>
            {
                p.StripePaymentIntentId = checkoutSession?.PaymentIntentId ?? "pi_test_sub";
                p.StripeFee = 0.45m;
                p.NetAmount = 4.55m;
            })
            .Returns(Task.CompletedTask);

        var sut = CreateService(db, stripe.Object, invoiceService.Object);

        var loadedPayment = await db.PaymentTransactions
            .Include(x => x.PricingPlan)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.ClientApplication)
            .FirstAsync(x => x.Id == payment.Id);

        var result = await sut.TryCompleteFromCheckoutSessionAsync(loadedPayment, session);

        Assert.True(result);
        var updated = await db.PaymentTransactions.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, updated!.Status);
        Assert.Equal("pi_test_sub", updated.StripePaymentIntentId);
        Assert.Equal(0.45m, updated.StripeFee);
        Assert.Equal(4.55m, updated.NetAmount);
    }

    private static WebhookService CreateService(ApplicationDbContext db, IStripePaymentDetailsService stripe)
    {
        return CreateService(db, stripe, Mock.Of<IInvoiceService>());
    }

    private static WebhookService CreateService(
        ApplicationDbContext db,
        IStripePaymentDetailsService stripe,
        IInvoiceService invoiceService)
    {
        return new WebhookService(
            db,
            Mock.Of<IAuditService>(),
            invoiceService,
            stripe,
            Mock.Of<ICollectedTaxService>(),
            Mock.Of<IStripeSettingsProvider>(),
            Mock.Of<ILogger<WebhookService>>());
    }

    private static async Task InvokeTryFinalizeFromStripeSubscriptionAsync(
        WebhookService sut,
        StripeSubscription stripeSubscription)
    {
        var method = typeof(WebhookService).GetMethod(
            "TryFinalizeFromStripeSubscriptionAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("TryFinalizeFromStripeSubscriptionAsync introuvable");

        var task = (Task)method.Invoke(sut, [stripeSubscription, CancellationToken.None])!;
        await task;
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
        string paymentCode,
        string stripeSessionId)
    {
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
            StripeCheckoutSessionId = stripeSessionId,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }
}
