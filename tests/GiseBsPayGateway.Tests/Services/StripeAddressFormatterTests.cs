using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;

namespace GiseBsPayGateway.Tests.Services;

public class StripeAddressFormatterTests
{
    [Theory]
    [InlineData("Ontario", "ON")]
    [InlineData("ontario", "ON")]
    [InlineData("ON", "ON")]
    [InlineData("Québec", "QC")]
    [InlineData("Quebec", "QC")]
    public void Formats_Canadian_Province_Names_To_Codes(string input, string expected)
    {
        var result = StripeAddressFormatter.Format(new BillingAddressDto(
            "100 Queen St W",
            null,
            "Toronto",
            input,
            "M5H 2N2",
            "CA"));

        Assert.Equal(expected, result.State);
        Assert.Equal("CA", result.Country);
        Assert.Equal("M5H 2N2", result.PostalCode);
    }

    [Theory]
    [InlineData("m5h2n2", "M5H 2N2")]
    [InlineData("M5H 2N2", "M5H 2N2")]
    public void Formats_Canadian_Postal_Code(string input, string expected)
    {
        var result = StripeAddressFormatter.Format(new BillingAddressDto(
            "100 Queen St W",
            null,
            "Toronto",
            "ON",
            input,
            "CA"));

        Assert.Equal(expected, result.PostalCode);
    }

    [Theory]
    [InlineData("California", "CA")]
    [InlineData("ca", "CA")]
    public void Formats_Us_State_Names_To_Codes(string input, string expected)
    {
        var result = StripeAddressFormatter.Format(new BillingAddressDto(
            "920 5th Ave",
            null,
            "San Francisco",
            input,
            "94103",
            "US"));

        Assert.Equal(expected, result.State);
    }
}
