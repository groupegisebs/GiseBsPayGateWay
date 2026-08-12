using GiseBsPayGateway.Services.Tax;

namespace GiseBsPayGateway.Tests.Services;

public class AfricanTaxServiceTests
{
    private readonly AfricanTaxService _sut = new();

    [Fact]
    public void Cameroon_Vat_1925_Inclusive()
    {
        var result = _sut.Calculate(5000m, "XAF", "CM");
        Assert.Equal(5000m, result.AmountExclusive);
        Assert.Equal(963m, result.TaxAmount);
        Assert.Equal(5963m, result.AmountInclusive);
        Assert.Equal(19.25m, result.TaxRatePercent);
        Assert.Equal("TVA", result.TaxName);
    }

    [Theory]
    [InlineData("SN", 18.00)]
    [InlineData("CI", 18.00)]
    [InlineData("NG", 7.50)]
    [InlineData("ZA", 15.00)]
    [InlineData("KE", 16.00)]
    [InlineData("GH", 15.00)]
    [InlineData("MA", 20.00)]
    [InlineData("EG", 14.00)]
    public void KnownAfricaRates(string country, decimal expectedRate)
    {
        var result = _sut.Calculate(10000m, "XAF", country);
        Assert.Equal(expectedRate, result.TaxRatePercent);
        Assert.Equal(10000m, result.AmountExclusive);
        Assert.True(result.AmountInclusive >= result.AmountExclusive);
        Assert.Equal(result.AmountExclusive + result.TaxAmount, result.AmountInclusive);
    }

    [Fact]
    public void ListRates_ContainsAllMajorCountries()
    {
        var rates = _sut.ListRates();
        Assert.True(rates.Count >= 50);
        Assert.Contains(rates, r => r.CountryCode == "CM" && r.RatePercent == 19.25m);
        Assert.Contains(rates, r => r.CountryCode == "SN");
        Assert.Contains(rates, r => r.CountryCode == "NG");
        Assert.Contains(rates, r => r.CountryCode == "ZA");
    }

    [Fact]
    public void UnknownCountry_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _sut.Calculate(1000m, "XAF", "XX"));
    }

    [Fact]
    public void AmountToPay_AlwaysIncludesTax()
    {
        foreach (var rate in AfricanTaxRates.AllOrdered())
        {
            var quote = _sut.Calculate(1000m, "XAF", rate.CountryCode);
            Assert.Equal(quote.AmountExclusive + quote.TaxAmount, quote.AmountInclusive);
            if (rate.RatePercent == 0)
                Assert.Equal(quote.AmountExclusive, quote.AmountInclusive);
            else
                Assert.True(quote.AmountInclusive > quote.AmountExclusive);
        }
    }
}
