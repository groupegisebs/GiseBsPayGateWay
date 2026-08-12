using GiseBsPayGateway.Controllers.Api;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Services.Tax;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GiseBsPayGateway.Tests.Api;

public class TaxControllerTests
{
    [Fact]
    public async Task Calculate_ResultatValide_Retourne200()
    {
        await using var db = TestDbContextFactory.Create(nameof(Calculate_ResultatValide_Retourne200));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var response = new TaxCalculationResponse(
            "CA-QC",
            0.14975m,
            ["gst", "qst"],
            [
                new TaxComponentDto("ca_gst", "GST", 0.05m, "federal"),
                new TaxComponentDto("ca_qst", "QST", 0.09975m, "provincial")
            ]);

        var taxService = new Mock<ITaxService>();
        taxService.Setup(s => s.CalculateAsync(It.IsAny<TaxCalculationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var controller = new TaxController(taxService.Object, Mock.Of<ICollectedTaxService>(), new AfricanTaxService());
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var request = new TaxCalculationRequest(
            new BillingAddressDto("1200 rue Edison", null, "Québec", "QC", "G3K 0P6", "CA"),
            "cad",
            10000);

        var result = await controller.Calculate(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<TaxCalculationResponse>(ok.Value);
        Assert.Equal("CA-QC", payload.JurisdictionCode);
        Assert.Equal("stripe", payload.Source);
    }

    [Fact]
    public async Task Calculate_AdresseInvalide_Retourne400()
    {
        await using var db = TestDbContextFactory.Create(nameof(Calculate_AdresseInvalide_Retourne400));
        var (app, _, apiKey) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);

        var taxService = new Mock<ITaxService>();
        taxService.Setup(s => s.CalculateAsync(It.IsAny<TaxCalculationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaxCalculationException("Adresse de facturation incomplète."));

        var controller = new TaxController(taxService.Object, Mock.Of<ICollectedTaxService>(), new AfricanTaxService());
        ControllerTestHelper.SetClientApplicationContext(controller, app, apiKey);

        var request = new TaxCalculationRequest(
            new BillingAddressDto("", null, "Québec", "QC", "G3K 0P6", "CA"));

        var result = await controller.Calculate(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("Adresse de facturation incomplète.", error.Error);
    }

    [Fact]
    public void AfricaRates_ListeComplete()
    {
        var controller = new TaxController(Mock.Of<ITaxService>(), Mock.Of<ICollectedTaxService>(), new AfricanTaxService());
        var result = controller.ListAfricaRates();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rates = Assert.IsAssignableFrom<IReadOnlyList<AfricanTaxRateDto>>(ok.Value);
        Assert.True(rates.Count >= 50);
        Assert.Contains(rates, r => r.CountryCode == "CM" && r.RatePercent == 0m);
    }

    [Fact]
    public void AfricaQuote_Cameroon_Exempt_TtcEqualsHt()
    {
        var controller = new TaxController(Mock.Of<ITaxService>(), Mock.Of<ICollectedTaxService>(), new AfricanTaxService());
        var result = controller.QuoteAfrica(new AfricanTaxQuoteRequest(5000m, "XAF", "CM"));
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var quote = Assert.IsType<AfricanTaxQuoteResponse>(ok.Value);
        Assert.Equal(5000m, quote.AmountInclusive);
        Assert.Equal(0m, quote.TaxAmount);
    }
}
