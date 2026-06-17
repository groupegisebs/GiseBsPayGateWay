using GiseBsPayGateway.Controllers.Api;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GiseBsPayGateway.Tests.Api;

public class CheckoutControllerTests
{
    [Fact]
    public async Task CreateSession_ErreurMetier_Retourne400()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateSession_ErreurMetier_Retourne400));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var paymentService = new Mock<IPaymentService>();
        paymentService.Setup(s => s.CreateCheckoutSessionAsync(app, It.IsAny<CreateCheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Produit 'X' introuvable."));

        var controller = new CheckoutController(paymentService.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.CreateSession(
            new CreateCheckoutSessionRequest("C1", "a@b.com", null, null, "X", "MONTHLY", "https://ok", "https://ko", null, null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Contains("introuvable", error.Error);
    }

    [Fact]
    public async Task CreateSession_Succes_Retourne200()
    {
        await using var db = TestDbContextFactory.Create(nameof(CreateSession_Succes_Retourne200));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var expected = new CheckoutSessionResponse("PAY-1", "https://stripe.test", "cs_1", "Pending");
        var paymentService = new Mock<IPaymentService>();
        paymentService.Setup(s => s.CreateCheckoutSessionAsync(app, It.IsAny<CreateCheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = new CheckoutController(paymentService.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.CreateSession(
            new CreateCheckoutSessionRequest("C1", "a@b.com", null, null, "AGENT-CODE", "MONTHLY", "https://ok", "https://ko", null, null),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CheckoutSessionResponse>(ok.Value);
        Assert.Equal("PAY-1", response.PaymentCode);
    }
}
