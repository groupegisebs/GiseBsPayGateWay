using GiseBsPayGateway.Controllers.Api;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace GiseBsPayGateway.Tests.Api;

public class AuthControllerTests
{
    [Fact]
    public async Task GetToken_AppInvalide_Retourne401()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetToken_AppInvalide_Retourne401));
        var sut = CreateController(db);

        var result = await sut.GetToken(new JwtTokenRequest("UNKNOWN", "key"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("Application invalide.", error.Error);
    }

    [Fact]
    public async Task GetToken_CleInvalide_Retourne401()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetToken_CleInvalide_Retourne401));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var sut = CreateController(db);

        var result = await sut.GetToken(new JwtTokenRequest(app.AppCode, "wrong-key"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("API Key invalide.", error.Error);
    }

    [Fact]
    public async Task GetToken_CredentialsValides_RetourneToken()
    {
        await using var db = TestDbContextFactory.Create(nameof(GetToken_CredentialsValides_RetourneToken));
        var (app, rawKey, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var sut = CreateController(db);

        var result = await sut.GetToken(new JwtTokenRequest(app.AppCode, rawKey), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var token = Assert.IsType<JwtTokenResponse>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresAt > DateTime.UtcNow);
    }

    private static AuthController CreateController(ApplicationDbContext db)
    {
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = "GiseBsPayGateway-Test",
            Audience = "GiseBsPayGatewayClients-Test",
            SecretKey = "test-secret-key-at-least-32-chars!",
            ExpirationMinutes = 60
        });

        return new AuthController(
            db,
            new ApiKeyService(),
            new JwtTokenService(jwtOptions),
            Mock.Of<IAuditService>());
    }
}
