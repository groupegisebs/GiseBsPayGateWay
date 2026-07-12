using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Http;
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

    [Fact]
    public void Scope_overrides_accessor_without_header()
    {
        var accessor = new HttpStripeEnvironmentAccessor(new HttpContextAccessor());
        Assert.False(accessor.UseTestMode);

        using (StripeEnvironmentScope.Begin(useTestMode: true))
        {
            Assert.True(accessor.UseTestMode);
            Assert.True(StripeEnvironmentScope.CurrentUseTestMode);
        }

        Assert.False(accessor.UseTestMode);
        Assert.Null(StripeEnvironmentScope.CurrentUseTestMode);
    }
}
