using GiseBsPayGateway.Services;
using Xunit;

namespace GiseBsPayGateway.Tests.Services;

public class StripeEnvironmentTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("PROD", false)]
    [InlineData("LIVE", false)]
    [InlineData("DEV", true)]
    [InlineData("dev", true)]
    [InlineData("TEST", true)]
    [InlineData(" test ", true)]
    public void IsDevRequest_resolves_header(string? value, bool expected)
    {
        Assert.Equal(expected, StripeEnvironment.IsDevRequest(value));
    }
}
