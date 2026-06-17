using System.Net;
using System.Net.Http.Json;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GiseBsPayGateway.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<PayGatewayWebApplicationFactory>
{
    private readonly PayGatewayWebApplicationFactory _factory;

    public ApiIntegrationTests(PayGatewayWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Retourne200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostCheckoutSession_SansAuth_Retourne401()
    {
        var client = _factory.CreateClient();
        var body = new CreateCheckoutSessionRequest(
            "C1", "a@b.com", null, null, "AGENT-CODE", "MONTHLY",
            "https://ok", "https://ko", null, null);

        var response = await client.PostAsJsonAsync("/api/checkout/session", body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAuthToken_CredentialsValides_RetourneJwt()
    {
        var (appCode, rawKey) = await SeedTestAppAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new JwtTokenRequest(appCode, rawKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<JwtTokenResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
    }

    [Fact]
    public async Task PostCheckoutSession_AvecJwtEtCatalogue_Retourne200()
    {
        var (appCode, rawKey) = await SeedTestAppAsync();
        await SeedCatalogAsync(appCode);

        var client = _factory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new JwtTokenRequest(appCode, rawKey));
        var token = await tokenResponse.Content.ReadFromJsonAsync<JwtTokenResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var checkoutBody = new CreateCheckoutSessionRequest(
            "CUST-INT-1", "integration@test.com", "Integration Test", null,
            "AGENT-CODE", "MONTHLY", "https://ok", "https://ko", null, null, Embedded: true);

        var response = await client.PostAsJsonAsync("/api/checkout/session", checkoutBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>();
        Assert.NotNull(session);
        Assert.StartsWith($"PAY-{appCode}-", session.PaymentCode);
        Assert.Equal("cs_secret_integration", session.ClientSecret);
    }

    [Fact]
    public async Task PostCheckoutSession_ProduitInexistant_Retourne400Json()
    {
        var (appCode, rawKey) = await SeedTestAppAsync();
        var client = _factory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new JwtTokenRequest(appCode, rawKey));
        var token = await tokenResponse.Content.ReadFromJsonAsync<JwtTokenResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var checkoutBody = new CreateCheckoutSessionRequest(
            "C1", "a@b.com", null, null, "INEXISTANT", "MONTHLY",
            "https://ok", "https://ko", null, null);

        var response = await client.PostAsJsonAsync("/api/checkout/session", checkoutBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Contains("INEXISTANT", error.Error);
    }

    private async Task<(string AppCode, string RawApiKey)> SeedTestAppAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (app, rawKey, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db, $"TEST-{Guid.NewGuid():N}"[..20]);
        return (app.AppCode, rawKey);
    }

    private async Task SeedCatalogAsync(string appCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var app = db.ClientApplications.Single(x => x.AppCode == appCode);
        await TestDbContextFactory.SeedProductPlanAsync(db, app);
    }
}
