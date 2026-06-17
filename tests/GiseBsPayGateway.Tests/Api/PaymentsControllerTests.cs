using GiseBsPayGateway.Controllers.Api;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GiseBsPayGateway.Tests.Api;

public class PaymentsControllerTests
{
    [Fact]
    public async Task GetPayment_PaiementInexistant_Retourne404()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetPayment_PaiementInexistant_Retourne404));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var paymentService = new Mock<IPaymentService>();
        paymentService.Setup(s => s.GetPaymentByCodeAsync(app, "PAY-UNKNOWN", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentResponse?)null);

        var controller = new PaymentsController(paymentService.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.GetPayment("PAY-UNKNOWN", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("Paiement introuvable.", error.Error);
    }

    [Fact]
    public async Task GetPayment_PaiementExistant_Retourne200()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetPayment_PaiementExistant_Retourne200));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var payment = new PaymentResponse(
            "PAY-1", "Paid", 24m, "USD", "C1", "AGENT-CODE", "MONTHLY",
            DateTime.UtcNow, DateTime.UtcNow, null,
            "cs_test_1", "pi_test_1", "G-20260617-AB12CD34EF",
            "https://pay.test/api/invoices/G-20260617-AB12CD34EF/download",
            AmountSubtotal: 24m,
            TaxAmount: 3.12m,
            GrossAmount: 27.12m,
            StripeFee: 1.09m,
            NetAmount: 26.03m,
            StripeBalanceTransactionId: "txn_test_1",
            BillingCountry: "CA",
            BillingState: "QC");
        var paymentService = new Mock<IPaymentService>();
        paymentService.Setup(s => s.GetPaymentByCodeAsync(app, "PAY-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var controller = new PaymentsController(paymentService.Object);
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var result = await controller.GetPayment("PAY-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PaymentResponse>(ok.Value);
        Assert.Equal("Paid", response.Status);
        Assert.Equal("G-20260617-AB12CD34EF", response.InvoiceNumber);
        Assert.Equal(3.12m, response.TaxAmount);
        Assert.Equal(1.09m, response.StripeFee);
        Assert.Equal("txn_test_1", response.StripeBalanceTransactionId);
    }
}
