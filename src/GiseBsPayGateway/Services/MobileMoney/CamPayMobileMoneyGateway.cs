using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services.MobileMoney;

/// <summary>
/// Adaptateur CamPay — endpoints publics documentés uniquement :
/// POST /token/, POST /collect/, GET /transaction/{reference}/.
/// </summary>
public sealed class CamPayMobileMoneyGateway : IMobileMoneyGateway
{
    public const string Code = "campay";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MobileMoneyOptions _options;
    private readonly CamPaySecretsOptions _secrets;
    private readonly ILogger<CamPayMobileMoneyGateway> _logger;
    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> _tokenCache = new();

    public CamPayMobileMoneyGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<MobileMoneyOptions> options,
        IOptions<CamPaySecretsOptions> secrets,
        ILogger<CamPayMobileMoneyGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _secrets = secrets.Value;
        _logger = logger;
    }

    public string ProviderCode => Code;

    public async Task<PaymentInitiationResult> InitiateAsync(
        MobileMoneyPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfiguredForLiveCalls();

        var client = CreateClient();
        var token = await GetTokenAsync(client, cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "collect/");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        httpRequest.Content = JsonContent.Create(new
        {
            amount = ((int)decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero)).ToString(),
            currency = request.Currency.ToUpperInvariant(),
            from = request.PhoneNumber,
            description = request.Description,
            external_reference = request.InternalReference
        }, options: JsonOpts);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CamPay collect failed HTTP {Status}", (int)response.StatusCode);
            return new PaymentInitiationResult(
                false, null, PaymentStatus.Failed, null, null, null,
                "PROVIDER_ERROR", "Échec d'initiation CamPay.");
        }

        var parsed = JsonSerializer.Deserialize<CamPayCollectResponse>(body, JsonOpts);
        var reference = parsed?.Reference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new PaymentInitiationResult(
                false, null, PaymentStatus.Failed, null, null, null,
                "PROVIDER_ERROR", "Référence CamPay manquante.");
        }

        var ussd = string.IsNullOrWhiteSpace(parsed?.UssdCode)
            ? (request.Channel == "ORANGE" ? "#150*50#" : "*126#")
            : parsed.UssdCode;

        return new PaymentInitiationResult(
            true,
            reference,
            PaymentStatus.PendingCustomerConfirmation,
            "PENDING",
            "Consultez votre téléphone et confirmez la demande de paiement. Ne communiquez jamais votre code secret Mobile Money.",
            ussd,
            null,
            null);
    }

    public async Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfiguredForLiveCalls();

        var client = CreateClient();
        var token = await GetTokenAsync(client, cancellationToken);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"transaction/{Uri.EscapeDataString(providerReference)}/");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Token", token);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaymentStatusResult(
                false, PaymentStatus.RequiresReview, null, null, null, null,
                "PROVIDER_ERROR", "Impossible de lire le statut CamPay.");
        }

        var parsed = JsonSerializer.Deserialize<CamPayTransactionResponse>(body, JsonOpts);
        var raw = parsed?.Status ?? "UNKNOWN";
        var normalized = MobileMoneyPhoneValidator.MapCamPayStatus(raw);
        decimal? amount = null;
        if (decimal.TryParse(parsed?.Amount?.ToString(), out var amt))
            amount = amt;

        return new PaymentStatusResult(
            true, normalized, raw, amount, parsed?.Currency, parsed?.Operator, null, null);
    }

    public Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(
            false, true, null, null,
            "Remboursement API CamPay non confirmé contractuellement — NotSupported."));

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        // Contrat exact de signature à confirmer avec CamPay.
        // Si un secret est configuré, exiger l'en-tête X-CamPay-Signature = HMAC-SHA256(body, secret).
        if (string.IsNullOrWhiteSpace(_secrets.WebhookSecret) ||
            _secrets.WebhookSecret.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            // Mode Local / secrets absents : accepter uniquement si Environment=Local (orchestrateur).
            var env = _options.Providers.CamPay.Environment;
            if (env.Equals("Local", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new WebhookValidationResult(true, null));

            return Task.FromResult(new WebhookValidationResult(
                false, "WebhookSecret CamPay non configuré."));
        }

        if (!request.Headers.TryGetValue("X-CamPay-Signature", out var signatureHeader))
            return Task.FromResult(new WebhookValidationResult(false, "Signature manquante."));

        return Task.FromResult(new WebhookValidationResult(true, null));
    }

    public async Task<MobileMoneyWebhookEventModel> ParseWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(_secrets.WebhookSecret) &&
            !_secrets.WebhookSecret.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) &&
            request.Headers.TryGetValue("X-CamPay-Signature", out var signatureHeader))
        {
            var expected = ComputeHmacHex(_secrets.WebhookSecret, payload);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(signatureHeader.ToString().Trim())))
            {
                throw new InvalidOperationException("Signature webhook CamPay invalide.");
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var parsed = JsonSerializer.Deserialize<CamPayWebhookPayload>(payload, JsonOpts);
        var raw = parsed?.Status ?? "UNKNOWN";
        var normalized = MobileMoneyPhoneValidator.MapCamPayStatus(raw);
        decimal? amount = null;
        if (decimal.TryParse(parsed?.Amount?.ToString(), out var amt))
            amount = amt;

        return new MobileMoneyWebhookEventModel(
            parsed?.Reference ?? parsed?.ExternalReference,
            "transaction.status",
            parsed?.Reference,
            parsed?.ExternalReference,
            normalized,
            raw,
            amount,
            parsed?.Currency,
            parsed?.Operator,
            hash,
            payload);
    }

    public Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var enabled = _options.Providers.CamPay.Enabled;
        var env = _options.Providers.CamPay.Environment;
        if (!enabled)
            return Task.FromResult(new ProviderHealthResult(false, "CamPay désactivé."));

        if (env.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ProviderHealthResult(true, "CamPay Local (simulateur)."));

        if (string.IsNullOrWhiteSpace(_secrets.Username) ||
            _secrets.Username.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ProviderHealthResult(false, "Secrets CamPay manquants."));

        return Task.FromResult(new ProviderHealthResult(true, $"CamPay {env} configuré."));
    }

    private void EnsureConfiguredForLiveCalls()
    {
        var env = _options.Providers.CamPay.Environment;
        if (env.Equals("Local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CamPayLive ne doit pas être appelé en Environment=Local.");

        if (string.IsNullOrWhiteSpace(_secrets.Username) ||
            string.IsNullOrWhiteSpace(_secrets.Password) ||
            _secrets.Username.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Credentials CamPay absents (secrets.json).");
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("CamPay");
        var baseUrl = ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.Providers.CamPay.BaseUrl))
            return _options.Providers.CamPay.BaseUrl;

        return _options.Providers.CamPay.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
            ? "https://campay.net/api"
            : "https://demo.campay.net/api";
    }

    private async Task<string> GetTokenAsync(HttpClient client, CancellationToken ct)
    {
        var cacheKey = ResolveBaseUrl();
        if (_tokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            return cached.Token;

        using var response = await client.PostAsJsonAsync("token/", new
        {
            username = _secrets.Username,
            password = _secrets.Password
        }, JsonOpts, ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CamPayTokenResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Réponse token CamPay invalide.");

        if (string.IsNullOrWhiteSpace(body.Token))
            throw new InvalidOperationException("Jeton CamPay vide.");

        _tokenCache[cacheKey] = (body.Token, DateTime.UtcNow.AddMinutes(50));
        return body.Token;
    }

    private static string ComputeHmacHex(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class CamPayTokenResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    private sealed class CamPayCollectResponse
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("ussd_code")]
        public string? UssdCode { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }
    }

    private sealed class CamPayTransactionResponse
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public object? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }

        [JsonPropertyName("external_reference")]
        public string? ExternalReference { get; set; }
    }

    private sealed class CamPayWebhookPayload
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("external_reference")]
        public string? ExternalReference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public object? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }
    }
}
