using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Moq;

namespace GiseBsPayGateway.Tests.Services;

public class PaymentServiceTests
{
    [Fact]
    public async Task CreateCheckoutSession_ProduitInexistant_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCheckoutSession_ProduitInexistant_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var stripe = new Mock<IStripeService>();
        var sut = CreatePaymentService(db, stripe);

        var request = CreateCheckoutRequest(productCode: "INEXISTANT");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CreateCheckoutSessionAsync(app, request));

        Assert.Contains("INEXISTANT", ex.Message);
        stripe.Verify(s => s.CreateCheckoutSessionAsync(
            It.IsAny<PaymentTransaction>(), It.IsAny<Customer>(), It.IsAny<PricingPlan>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateCheckoutSession_PlanInexistant_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCheckoutSession_PlanInexistant_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app, planCode: "MONTHLY");

        var stripe = new Mock<IStripeService>();
        var sut = CreatePaymentService(db, stripe);

        var request = CreateCheckoutRequest(planCode: "YEARLY");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CreateCheckoutSessionAsync(app, request));

        Assert.Contains("YEARLY", ex.Message);
    }

    [Fact]
    public async Task CreateCheckoutSession_Succes_CreeClientEtPaiement()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCheckoutSession_Succes_CreeClientEtPaiement));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app);

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.CreateCheckoutSessionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<Customer>(), It.IsAny<PricingPlan>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("cs_test_1", "https://checkout.stripe.com/x", null));

        var sut = CreatePaymentService(db, stripe);

        var result = await sut.CreateCheckoutSessionAsync(app, CreateCheckoutRequest());

        Assert.StartsWith("PAY-BOUTIQUEGISE-", result.PaymentCode);
        Assert.Equal("cs_test_1", result.SessionId);
        Assert.Equal("Pending", result.Status);
        Assert.Single(db.Customers);
        Assert.Single(db.PaymentTransactions);
    }

    [Fact]
    public async Task CreateCheckoutSession_Embedded_RetourneClientSecretEtPublishableKey()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCheckoutSession_Embedded_RetourneClientSecretEtPublishableKey));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app);

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.CreateCheckoutSessionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<Customer>(), It.IsAny<PricingPlan>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("cs_test_1", null, "cs_secret_1"));

        var settings = new Mock<IStripeSettingsProvider>();
        settings.Setup(s => s.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeSettingsSnapshot("pk_test_xxx", "sk_test", "whsec", false, false));

        var sut = new PaymentService(db, stripe.Object, settings.Object, Mock.Of<IAuditService>());

        var result = await sut.CreateCheckoutSessionAsync(app, CreateCheckoutRequest(embedded: true));

        Assert.Equal("cs_secret_1", result.ClientSecret);
        Assert.Equal("pk_test_xxx", result.PublishableKey);
    }

    [Fact]
    public async Task CreateCheckoutSession_ClientExistant_MetAJourEmail()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCheckoutSession_ClientExistant_MetAJourEmail));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app);

        db.Customers.Add(new Customer
        {
            ClientApplicationId = app.Id,
            CustomerCode = "CUST-1",
            Email = "old@test.com",
            FullName = "Old Name"
        });
        await db.SaveChangesAsync();

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.CreateCheckoutSessionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<Customer>(), It.IsAny<PricingPlan>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("cs_test_1", "https://checkout.stripe.com/x", null));

        var sut = CreatePaymentService(db, stripe);
        var request = CreateCheckoutRequest(email: "new@test.com", fullName: "New Name");

        await sut.CreateCheckoutSessionAsync(app, request);

        var customer = db.Customers.Single();
        Assert.Equal("new@test.com", customer.Email);
        Assert.Equal("New Name", customer.FullName);
        Assert.Single(db.Customers);
    }

    [Fact]
    public async Task GetPaymentByCodeAsync_PaiementInexistant_RetourneNull()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetPaymentByCodeAsync_PaiementInexistant_RetourneNull));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var sut = CreatePaymentService(db, new Mock<IStripeService>());

        var result = await sut.GetPaymentByCodeAsync(app, "PAY-UNKNOWN");

        Assert.Null(result);
    }

    private static PaymentService CreatePaymentService(ApplicationDbContext db, Mock<IStripeService> stripe)
    {
        var settings = new Mock<IStripeSettingsProvider>();
        settings.Setup(s => s.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeSettingsSnapshot("pk_test", "sk_test", "whsec", false, false));

        return new PaymentService(db, stripe.Object, settings.Object, Mock.Of<IAuditService>());
    }

    private static CreateCheckoutSessionRequest CreateCheckoutRequest(
        string customerCode = "CUST-1",
        string email = "test@example.com",
        string? fullName = "Test User",
        string productCode = "AGENT-CODE",
        string planCode = "MONTHLY",
        bool embedded = false) =>
        new(customerCode, email, fullName, null, productCode, planCode,
            "https://success.test", "https://cancel.test", null, null, Embedded: embedded);
}
