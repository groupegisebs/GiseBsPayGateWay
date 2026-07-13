using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public interface IPayPalPayoutService
{
    bool IsConfigured { get; }
    string BuildAuthorizationUrl(string state, string? returnHint = null);
    Task<PayPalLinkedAccount> CompleteOAuthAsync(ClientApplication app, string code, string externalReference, CancellationToken ct = default);
    Task<PayPalLinkedAccount?> GetLinkedAsync(ClientApplication app, string externalReference, CancellationToken ct = default);
    Task<(bool Success, string? PayoutBatchId, string? Error)> SendPayoutAsync(
        string destinationEmailOrPayerId,
        long amountMinor,
        string currency,
        string idempotencyKey,
        string? note,
        CancellationToken ct = default);
}

public class PayPalPayoutService(
    ApplicationDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<PayPalOptions> options,
    ILogger<PayPalPayoutService> logger) : IPayPalPayoutService
{
    private readonly PayPalOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;

    public string BuildAuthorizationUrl(string state, string? returnHint = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("PayPal n'est pas configuré (PayPal:ClientId/Secret).");

        var redirect = _opts.OAuthRedirectUri
            ?? throw new InvalidOperationException("PayPal:OAuthRedirectUri manquant.");

        var scope = Uri.EscapeDataString("openid email https://uri.paypal.com/services/paypalattributes");
        return $"{_opts.AuthBaseUrl}/signin/authorize"
            + $"?client_id={Uri.EscapeDataString(_opts.ClientId!)}"
            + $"&response_type=code"
            + $"&scope={scope}"
            + $"&redirect_uri={Uri.EscapeDataString(redirect)}"
            + $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task<PayPalLinkedAccount> CompleteOAuthAsync(
        ClientApplication app,
        string code,
        string externalReference,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("PayPal n'est pas configuré.");

        var token = await ExchangeCodeAsync(code, ct);
        var userInfo = await GetUserInfoAsync(token.AccessToken, ct);

        var existing = await db.PayPalLinkedAccounts
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id
                && x.ExternalReference == externalReference, ct);

        if (existing is null)
        {
            existing = new PayPalLinkedAccount
            {
                ClientApplicationId = app.Id,
                ExternalReference = externalReference
            };
            db.PayPalLinkedAccounts.Add(existing);
        }

        existing.PayerId = userInfo.PayerId ?? userInfo.Sub;
        existing.MaskedEmail = MaskEmail(userInfo.Email);
        existing.RefreshTokenEncrypted = Encrypt(token.RefreshToken ?? token.AccessToken);
        existing.AccessTokenEncrypted = Encrypt(token.AccessToken);
        existing.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn - 60 : 3500);
        existing.Status = "linked";
        existing.LastVerifiedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public Task<PayPalLinkedAccount?> GetLinkedAsync(ClientApplication app, string externalReference, CancellationToken ct = default)
        => db.PayPalLinkedAccounts.FirstOrDefaultAsync(x =>
            x.ClientApplicationId == app.Id && x.ExternalReference == externalReference, ct);

    public async Task<(bool Success, string? PayoutBatchId, string? Error)> SendPayoutAsync(
        string destinationEmailOrPayerId,
        long amountMinor,
        string currency,
        string idempotencyKey,
        string? note,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return (false, null, "PayPal non configuré — laissez la demande en revue manuelle.");

        try
        {
            var accessToken = await GetClientCredentialsTokenAsync(ct);
            var client = httpClientFactory.CreateClient("PayPal");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var amount = (amountMinor / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            var body = new
            {
                sender_batch_header = new
                {
                    sender_batch_id = idempotencyKey.Length > 50 ? idempotencyKey[..50] : idempotencyKey,
                    email_subject = "Vous avez reçu un versement",
                    email_message = note ?? "Versement marketplace"
                },
                items = new[]
                {
                    new
                    {
                        recipient_type = destinationEmailOrPayerId.Contains('@') ? "EMAIL" : "PAYPAL_ID",
                        amount = new { value = amount, currency = currency.ToUpperInvariant() },
                        note = note ?? "Payout",
                        sender_item_id = idempotencyKey.Length > 30 ? idempotencyKey[..30] : idempotencyKey,
                        receiver = destinationEmailOrPayerId
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBaseUrl}/v1/payments/payouts")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

            var response = await client.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("PayPal payout failed {Status}: {Body}", (int)response.StatusCode, json);
                return (false, null, json);
            }

            using var doc = JsonDocument.Parse(json);
            var batchId = doc.RootElement.GetProperty("batch_header").GetProperty("payout_batch_id").GetString();
            return (true, batchId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PayPal payout error");
            return (false, null, ex.Message);
        }
    }

    private async Task<TokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PayPal");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBaseUrl}/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _opts.OAuthRedirectUri!
        });
        var response = await client.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"PayPal token exchange failed: {json}");
        return JsonSerializer.Deserialize<TokenResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Token PayPal invalide.");
    }

    private async Task<string> GetClientCredentialsTokenAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PayPal");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBaseUrl}/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });
        var response = await client.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"PayPal client credentials failed: {json}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token manquant");
    }

    private async Task<UserInfoResponse> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("PayPal");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_opts.ApiBaseUrl}/v1/identity/oauth2/userinfo?schema=paypalv1.1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new UserInfoResponse(null, null, null);
        return JsonSerializer.Deserialize<UserInfoResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new UserInfoResponse(null, null, null);
    }

    private string Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var key = DeriveKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    private byte[] DeriveKey()
    {
        var material = _opts.TokenEncryptionKey ?? _opts.ClientSecret ?? "paypal-local-dev-key";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return email;
        var parts = email.Split('@', 2);
        var local = parts[0];
        var masked = local.Length <= 2 ? "••" : $"{local[0]}••••{local[^1]}";
        return $"{masked}@{parts[1]}";
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("user_id")] string? PayerId,
        [property: JsonPropertyName("sub")] string? Sub,
        [property: JsonPropertyName("email")] string? Email);
}
