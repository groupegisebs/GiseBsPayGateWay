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
    public async Task PostCheckoutSession_AvecApiKeyEtCatalogue_Retourne200()
    {
        var (appCode, rawKey) = await SeedTestAppAsync();
        await SeedCatalogAsync(appCode);

        var client = CreateApiKeyClient(appCode, rawKey);

        var checkoutBody = new CreateCheckoutSessionRequest(
            "CUST-INT-1", "integration@test.com", "Integration Test", null,
            "AGENT-CODE", "MONTHLY", "https://ok", "https://ko", null, null, Embedded: true);

        var response = await client.PostAsJsonAsync("/api/checkout/session", checkoutBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>();
        Assert.NotNull(session);
        Assert.StartsWith($"PAY-{appCode.ToUpperInvariant()}-", session.PaymentCode);
        Assert.Equal("cs_secret_integration", session.ClientSecret);
    }

    [Fact]
    public async Task PostCheckoutSession_ProduitInexistant_Retourne400Json()
    {
        var (appCode, rawKey) = await SeedTestAppAsync();
        var client = CreateApiKeyClient(appCode, rawKey);

        var checkoutBody = new CreateCheckoutSessionRequest(
            "C1", "a@b.com", null, null, "INEXISTANT", "MONTHLY",
            "https://ok", "https://ko", null, null);

        var response = await client.PostAsJsonAsync("/api/checkout/session", checkoutBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Contains("INEXISTANT", error.Error);
    }

    [Fact]
    public async Task PostCatalogItem_AvecApiKey_CreeProduitEtPlan()
    {
        var (appCode, rawKey) = await SeedTestAppAsync();
        var client = CreateApiKeyClient(appCode, rawKey);

        var request = new CreateCatalogItemRequest(
            "API-CAT-1", "Produit via API", "Créé par test intégration",
            "MONTHLY", "Plan mensuel", 19m, "USD", SyncToStripe: false);

        var response = await client.PostAsJsonAsync("/api/products/catalog", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<CatalogItemResponse>();
        Assert.NotNull(item);
        Assert.Equal("API-CAT-1", item.Product.ProductCode);
        Assert.Equal("MONTHLY", item.Plan.PlanCode);
    }

    [Fact]
    public async Task PostProductsCatalog_SansAuth_Retourne401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/products/catalog", new CreateCatalogItemRequest(
            "X", "X", null, "MONTHLY", "M", 1m, "USD"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient CreateApiKeyClient(PayGatewayWebApplicationFactory factory, string appCode, string rawKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-App-Code", appCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", rawKey);
        return client;
    }

    private HttpClient CreateApiKeyClient(string appCode, string rawKey) =>
        CreateApiKeyClient(_factory, appCode, rawKey);

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
