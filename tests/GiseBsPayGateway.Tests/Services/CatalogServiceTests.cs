using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Moq;

namespace GiseBsPayGateway.Tests.Services;

public class CatalogServiceTests
{
    [Fact]
    public async Task CreateProductAsync_CreeProduitPourApplication()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateProductAsync_CreeProduitPourApplication));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var sut = new CatalogService(db, Mock.Of<IStripeService>(), Mock.Of<IAuditService>());

        var result = await sut.CreateProductAsync(app, new CreateProductRequest(
            "NEW-PRODUCT", "Nouveau produit", "Description test"));

        Assert.Equal("NEW-PRODUCT", result.ProductCode);
        Assert.Single(db.Products);
    }

    [Fact]
    public async Task CreateProductAsync_Duplicate_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateProductAsync_Duplicate_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app, "DUP-PROD");
        var sut = new CatalogService(db, Mock.Of<IStripeService>(), Mock.Of<IAuditService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateProductAsync(app, new CreateProductRequest("DUP-PROD", "Dup", null)));
    }

    [Fact]
    public async Task CreatePlanAsync_DeviseInvalide_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreatePlanAsync_DeviseInvalide_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app, "P1", "MONTHLY");
        var sut = new CatalogService(db, Mock.Of<IStripeService>(), Mock.Of<IAuditService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreatePlanAsync(app, "P1", new CreatePricingPlanRequest(
                "MONTHLY", "Mensuel", 10m, "$")));
    }

    [Fact]
    public async Task CreatePlanAsync_PlanCodeInvalide_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreatePlanAsync_PlanCodeInvalide_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app, "P1", "MONTHLY");
        var sut = new CatalogService(db, Mock.Of<IStripeService>(), Mock.Of<IAuditService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreatePlanAsync(app, "P1", new CreatePricingPlanRequest(
                "AGENT-CODE", "Bad", 10m, "USD")));
    }

    [Fact]
    public async Task CreateCatalogItemAsync_CreeProduitEtPlan()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCatalogItemAsync_CreeProduitEtPlan));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.EnsureStripeProductAsync(It.IsAny<Entities.Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("prod_test_1");
        stripe.Setup(s => s.EnsureStripePriceAsync(It.IsAny<Entities.PricingPlan>(), "prod_test_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("price_test_1");

        var sut = new CatalogService(db, stripe.Object, Mock.Of<IAuditService>());

        var result = await sut.CreateCatalogItemAsync(app, new CreateCatalogItemRequest(
            "API-PRODUCT", "Produit API", null,
            "MONTHLY", "Plan mensuel", 24m, "USD", SyncToStripe: true));

        Assert.Equal("prod_test_1", result.Product.StripeProductId);
        Assert.Equal("price_test_1", result.Plan.StripePriceId);
        Assert.Equal("API-PRODUCT", result.Product.ProductCode);
        Assert.Equal("MONTHLY", result.Plan.PlanCode);
    }

    [Fact]
    public async Task CreatePlanAsync_MemePlanCodeAutreDevise_Autorise()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreatePlanAsync_MemePlanCodeAutreDevise_Autorise));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app, "P1", "MONTHLY"); // USD
        var sut = new CatalogService(db, Mock.Of<IStripeService>(), Mock.Of<IAuditService>());

        var result = await sut.CreatePlanAsync(app, "P1", new CreatePricingPlanRequest(
            "MONTHLY", "Mensuel CAD", 30m, "CAD"));

        Assert.Equal("cad", result.Currency);
        Assert.Equal(2, db.PricingPlans.Count(x => x.IsActive));
    }

    [Fact]
    public async Task SyncProductToStripeAsync_SynchroniseProduitEtPlans()
    {
        await using var db = TestDbContextFactory.Create(nameof(SyncProductToStripeAsync_SynchroniseProduitEtPlans));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        await TestDbContextFactory.SeedProductPlanAsync(db, app, "VENDOR-CREATOR-PLAN", "MONTHLY", 5m);

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.EnsureStripeProductAsync(It.IsAny<Entities.Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("prod_sync_1");
        stripe.Setup(s => s.EnsureStripePriceAsync(It.IsAny<Entities.PricingPlan>(), "prod_sync_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("price_sync_1");

        var sut = new CatalogService(db, stripe.Object, Mock.Of<IAuditService>());

        var result = await sut.SyncProductToStripeAsync(app, "VENDOR-CREATOR-PLAN");

        Assert.Equal("prod_sync_1", result.StripeProductId);
        Assert.NotNull(result.Plans);
        Assert.Equal("price_sync_1", result.Plans!.Single().StripePriceId);
        stripe.Verify(s => s.EnsureStripeProductAsync(It.IsAny<Entities.Product>(), It.IsAny<CancellationToken>()), Times.Once);
        stripe.Verify(s => s.EnsureStripePriceAsync(It.IsAny<Entities.PricingPlan>(), "prod_sync_1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncProductToStripeAsync_ProduitInconnu_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(SyncProductToStripeAsync_ProduitInconnu_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var sut = new CatalogService(db, Mock.Of<IStripeService>(), Mock.Of<IAuditService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SyncProductToStripeAsync(app, "MISSING-PRODUCT"));
    }

    [Fact]
    public async Task SyncProductToStripeAsync_SansPlanActif_LeveInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create(nameof(SyncProductToStripeAsync_SansPlanActif_LeveInvalidOperationException));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        db.Products.Add(new Product
        {
            ClientApplicationId = app.Id,
            ProductCode = "NO-PLAN",
            Name = "No plan product",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.EnsureStripeProductAsync(It.IsAny<Entities.Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("prod_no_plan");

        var sut = new CatalogService(db, stripe.Object, Mock.Of<IAuditService>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SyncProductToStripeAsync(app, "NO-PLAN"));

        Assert.Contains("plan actif", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
