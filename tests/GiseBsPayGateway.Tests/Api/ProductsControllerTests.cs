using GiseBsPayGateway.Controllers.Api;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GiseBsPayGateway.Tests.Api;

public class ProductsControllerTests
{
    [Fact]
    public async Task CreateCatalogItem_Succes_Retourne201()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateCatalogItem_Succes_Retourne201));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var expected = new CatalogItemResponse(
            new ProductResponse("P1", "Produit", null, true, null, DateTime.UtcNow),
            new PricingPlanResponse("MONTHLY", "Mensuel", 24m, "usd", "Monthly", true, null, DateTime.UtcNow));

        var catalog = new Mock<ICatalogService>();
        catalog.Setup(s => s.CreateCatalogItemAsync(app, It.IsAny<CreateCatalogItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = new ProductsController(catalog.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.CreateCatalogItem(
            new CreateCatalogItemRequest("P1", "Produit", null, "MONTHLY", "Mensuel", 24m, "USD"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ProductsController.Get), created.ActionName);
    }

    [Fact]
    public async Task Create_ErreurMetier_Retourne400()
    {
        await using var db = TestDbContextFactory.Create(nameof(Create_ErreurMetier_Retourne400));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var catalog = new Mock<ICatalogService>();
        catalog.Setup(s => s.CreateProductAsync(app, It.IsAny<CreateProductRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Le produit 'X' existe déjà."));

        var controller = new ProductsController(catalog.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.Create(new CreateProductRequest("X", "X", null), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ApiErrorResponse>(badRequest.Value);
    }

    [Fact]
    public async Task SyncStripe_Succes_Retourne200()
    {
        await using var db = TestDbContextFactory.Create(nameof(SyncStripe_Succes_Retourne200));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var expected = new ProductResponse(
            "VENDOR-CREATOR-PLAN", "Seller plan", null, true, "prod_1", DateTime.UtcNow,
            [new PricingPlanResponse("MONTHLY", "Mensuel", 5m, "USD", "Monthly", true, "price_1", DateTime.UtcNow)]);

        var catalog = new Mock<ICatalogService>();
        catalog.Setup(s => s.SyncProductToStripeAsync(app, "VENDOR-CREATOR-PLAN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = new ProductsController(catalog.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.SyncStripe("VENDOR-CREATOR-PLAN", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expected, ok.Value);
    }
}
