using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GiseBsPayGateway.Tests.Services;

public class TaxServiceTests
{
    [Fact]
    public async Task CalculateAsync_AdresseCanadaSansProvince_LeveTaxCalculationException()
    {
        var sut = new TaxService(Mock.Of<IStripeSettingsProvider>(), NullLogger<TaxService>.Instance);
        var request = new TaxCalculationRequest(
            new BillingAddressDto("1200 rue Edison", null, "Québec", null, "G3K 0P6", "CA"));

        var ex = await Assert.ThrowsAsync<TaxCalculationException>(
            () => sut.CalculateAsync(request, CancellationToken.None));

        Assert.Equal("Province ou état requis pour cette adresse.", ex.Message);
    }

    [Fact]
    public async Task CalculateAsync_AdresseIncomplete_LeveTaxCalculationException()
    {
        var sut = new TaxService(Mock.Of<IStripeSettingsProvider>(), NullLogger<TaxService>.Instance);
        var request = new TaxCalculationRequest(
            new BillingAddressDto("", null, "Québec", "QC", "G3K 0P6", "CA"));

        var ex = await Assert.ThrowsAsync<TaxCalculationException>(
            () => sut.CalculateAsync(request, CancellationToken.None));

        Assert.Equal("Adresse de facturation incomplète.", ex.Message);
    }
}
