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
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(),
            It.IsAny<BillingAddressDto?>(), It.IsAny<CustomerUpdateDto?>(), It.IsAny<CancellationToken>()),
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(),
                It.IsAny<BillingAddressDto?>(), It.IsAny<CustomerUpdateDto?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), true,
                It.IsAny<BillingAddressDto?>(), It.IsAny<CustomerUpdateDto?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("cs_test_1", null, "cs_secret_1"));

        var settings = new Mock<IStripeSettingsProvider>();
        settings.Setup(s => s.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeSettingsSnapshot("pk_test_xxx", "sk_test", "whsec", false, false));

        var sut = new PaymentService(
            db,
            stripe.Object,
            settings.Object,
            Mock.Of<IAuditService>(),
            Mock.Of<IInvoiceService>(),
            Mock.Of<IInvoiceLinkBuilder>());

        var result = await sut.CreateCheckoutSessionAsync(app, CreateCheckoutRequest(embedded: true));

        Assert.Equal("cs_secret_1", result.ClientSecret);
        Assert.Equal("pk_test_xxx", result.PublishableKey);
    }

    [Fact]
    public async Task CreateCheckoutSession_AvecAdresseFacturation_TransmetAdresseAuServiceStripe()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCheckoutSession_AvecAdresseFacturation_TransmetAdresseAuServiceStripe));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app);

        var billingAddress = new BillingAddressDto(
            "1200 rue Edison",
            null,
            "Québec",
            "QC",
            "G3K 0P6",
            "CA");
        var customerUpdate = new CustomerUpdateDto("auto");

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.CreateCheckoutSessionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<Customer>(), It.IsAny<PricingPlan>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(),
                billingAddress, customerUpdate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("cs_test_1", "https://checkout.stripe.com/x", null));

        var sut = CreatePaymentService(db, stripe);
        var request = CreateCheckoutRequest() with
        {
            BillingAddress = billingAddress,
            CustomerUpdate = customerUpdate
        };

        var result = await sut.CreateCheckoutSessionAsync(app, request);

        Assert.StartsWith("PAY-BOUTIQUEGISE-", result.PaymentCode);
        var payment = db.PaymentTransactions.Single();
        Assert.Equal("CA", payment.BillingCountry);
        Assert.Equal("QC", payment.BillingState);
        stripe.Verify(s => s.CreateCheckoutSessionAsync(
            It.IsAny<PaymentTransaction>(), It.IsAny<Customer>(), It.IsAny<PricingPlan>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(),
            billingAddress, customerUpdate, It.IsAny<CancellationToken>()), Times.Once);
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(),
                It.IsAny<BillingAddressDto?>(), It.IsAny<CustomerUpdateDto?>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task GetPaymentByCodeAsync_PaiementReussi_ExposeFraisEtTaxes()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetPaymentByCodeAsync_PaiementReussi_ExposeFraisEtTaxes));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);

        var customer = new Customer
        {
            ClientApplicationId = app.Id,
            CustomerCode = "CUST-1",
            Email = "test@example.com"
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = "PAY-FEES-1",
            Status = PaymentStatus.Succeeded,
            Amount = 100m,
            Currency = "cad",
            AmountSubtotal = 100m,
            TaxAmount = 15m,
            GrossAmount = 115m,
            StripeFee = 3.35m,
            NetAmount = 111.65m,
            StripeBalanceTransactionId = "txn_abc",
            BillingCountry = "CA",
            BillingState = "ON",
            PaidAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();

        var sut = CreatePaymentService(db, new Mock<IStripeService>());
        var result = await sut.GetPaymentByCodeAsync(app, "PAY-FEES-1");

        Assert.NotNull(result);
        Assert.Equal(15m, result!.TaxAmount);
        Assert.Equal(115m, result.GrossAmount);
        Assert.Equal(3.35m, result.StripeFee);
        Assert.Equal(111.65m, result.NetAmount);
        Assert.Equal("txn_abc", result.StripeBalanceTransactionId);
        Assert.Equal("CA", result.BillingCountry);
        Assert.Equal("ON", result.BillingState);
    }

    private static PaymentService CreatePaymentService(ApplicationDbContext db, Mock<IStripeService> stripe)
    {
        var settings = new Mock<IStripeSettingsProvider>();
        settings.Setup(s => s.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeSettingsSnapshot("pk_test", "sk_test", "whsec", false, false));

        var invoiceService = new Mock<IInvoiceService>();
        invoiceService.Setup(s => s.GetByPaymentCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentInvoice?)null);
        invoiceService.Setup(s => s.EnsureInvoiceForPaymentAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentInvoice?)null);

        var invoiceLinks = new Mock<IInvoiceLinkBuilder>();
        invoiceLinks.Setup(l => l.BuildDownloadUrl(It.IsAny<string>()))
            .Returns((string code) => $"/api/invoices/{code}/download");

        return new PaymentService(
            db,
            stripe.Object,
            settings.Object,
            Mock.Of<IAuditService>(),
            invoiceService.Object,
            invoiceLinks.Object);
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
