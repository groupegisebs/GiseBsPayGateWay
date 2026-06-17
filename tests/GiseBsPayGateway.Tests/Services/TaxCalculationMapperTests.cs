using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using Stripe.Tax;

namespace GiseBsPayGateway.Tests.Services;

public class TaxCalculationMapperTests
{
    private static readonly BillingAddressDto QuebecAddress = new(
        "1200 rue Edison", null, "Québec", "QC", "G3K 0P6", "CA");

    [Fact]
    public void Map_Quebec_ReturnsGstAndQst()
    {
        var calculation = BuildCalculation(
            new CalculationTaxBreakdown
            {
                TaxRateDetails = new CalculationTaxBreakdownTaxRateDetails
                {
                    Country = "CA",
                    TaxType = "gst",
                    PercentageDecimal = "5.0"
                }
            },
            new CalculationTaxBreakdown
            {
                TaxRateDetails = new CalculationTaxBreakdownTaxRateDetails
                {
                    Country = "CA",
                    State = "QC",
                    TaxType = "qst",
                    PercentageDecimal = "9.975"
                }
            });

        var result = TaxCalculationMapper.Map(calculation, QuebecAddress);

        Assert.NotNull(result);
        Assert.Equal("CA-QC", result!.JurisdictionCode);
        Assert.Equal(2, result.Components.Count);
        Assert.Equal(0.14975m, result.EstimatedTaxRate);
        Assert.Contains(result.Components, c => c.Code == "ca_gst" && c.Rate == 0.05m);
        Assert.Contains(result.Components, c => c.Code == "ca_qst" && c.Rate == 0.09975m);
        Assert.Equal(["gst", "qst"], result.TaxLabels);
    }

    [Fact]
    public void Map_Ontario_ReturnsHst()
    {
        var address = new BillingAddressDto("100 Queen St W", null, "Toronto", "ON", "M5H 2N2", "CA");
        var calculation = BuildCalculation(new CalculationTaxBreakdown
        {
            TaxRateDetails = new CalculationTaxBreakdownTaxRateDetails
            {
                Country = "CA",
                State = "ON",
                TaxType = "hst",
                PercentageDecimal = "13.0"
            }
        });

        var result = TaxCalculationMapper.Map(calculation, address);

        Assert.NotNull(result);
        Assert.Equal("CA-ON", result!.JurisdictionCode);
        Assert.Single(result.Components);
        Assert.Equal(0.13m, result.EstimatedTaxRate);
    }

    [Fact]
    public void Map_EmptyBreakdown_ReturnsNull()
    {
        var calculation = new Calculation { TaxBreakdown = [] };

        var result = TaxCalculationMapper.Map(calculation, QuebecAddress);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("CA", "QC", "CA-QC")]
    [InlineData("US", "CA", "US-CA")]
    [InlineData("FR", null, "VAT-FR")]
    public void BuildJurisdictionCode_FormatsExpectedCodes(string country, string? state, string expected)
    {
        Assert.Equal(expected, TaxCalculationMapper.BuildJurisdictionCode(country, state));
    }

    [Theory]
    [InlineData("5.0", 0.05)]
    [InlineData("9.975", 0.09975)]
    [InlineData(null, 0)]
    public void ParsePercentageRate_ConvertsStripePercent(string? input, decimal expected)
    {
        Assert.Equal(expected, TaxCalculationMapper.ParsePercentageRate(input));
    }

    private static Calculation BuildCalculation(params CalculationTaxBreakdown[] items) =>
        new() { TaxBreakdown = items.ToList() };
}
