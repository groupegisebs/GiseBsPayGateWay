using System.Text;
using GiseBsPayGateway.Controllers.Api;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace GiseBsPayGateway.Tests.Api;

public class WebhooksControllerTests
{
    [Fact]
    public async Task Stripe_SignatureManquante_Retourne400()
    {
        var controller = CreateController(new Mock<IWebhookService>(), "{}");

        var result = await controller.Stripe(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Stripe_SignatureInvalide_Retourne401()
    {
        var webhook = new Mock<IWebhookService>();
        webhook.Setup(s => s.ProcessStripeWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var controller = CreateController(webhook, "{}", "sig_invalid");

        var result = await controller.Stripe(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Stripe_Succes_Retourne200()
    {
        var webhook = new Mock<IWebhookService>();
        webhook.Setup(s => s.ProcessStripeWebhookAsync(It.IsAny<string>(), "sig_valid", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(webhook, "{\"id\":\"evt_1\"}", "sig_valid");

        var result = await controller.Stripe(CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    private static WebhooksController CreateController(Mock<IWebhookService> webhook, string body, string? signature = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (signature is not null)
        {
            httpContext.Request.Headers["Stripe-Signature"] = signature;
        }

        return new WebhooksController(webhook.Object, Mock.Of<ILogger<WebhooksController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }
}
