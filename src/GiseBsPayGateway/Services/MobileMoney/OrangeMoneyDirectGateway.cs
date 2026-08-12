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
/// Orange Money Web Payment Cameroun.
/// OAuth : POST https://api.orange.com/oauth/v3/token
/// WebPay : POST https://api.orange.com/orange-money-webpay/cm/v1/webpayment
/// Retourne une payment_url (redirection client).
/// </summary>
public sealed class OrangeMoneyDirectGateway : IMobileMoneyGateway
{
    public const string Code = "orange_direct";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MobileMoneyOptions _options;
    private readonly OrangeSecretsOptions _secrets;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrangeMoneyDirectGateway> _logger;
    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> _tokenCache = new();

    public OrangeMoneyDirectGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<MobileMoneyOptions> options,
        IOptions<OrangeSecretsOptions> secrets,
        IConfiguration configuration,
        ILogger<OrangeMoneyDirectGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _secrets = secrets.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public string ProviderCode => Code;

    public async Task<PaymentInitiationResult> InitiateAsync(
        MobileMoneyPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (IsLocal())
            throw new InvalidOperationException("Orange live ne doit pas être appelé en Environment=Local (utiliser le simulateur).");

        EnsureSecrets();

        var returnUrl = FirstNonEmpty(request.ReturnUrl, _options.Providers.OrangeDirect.ReturnUrl);
        var cancelUrl = FirstNonEmpty(request.CancelUrl, _options.Providers.OrangeDirect.CancelUrl);
        var notifUrl = FirstNonEmpty(request.NotifUrl, _options.Providers.OrangeDirect.NotifUrl, DefaultNotifUrl());

        if (string.IsNullOrWhiteSpace(returnUrl) || string.IsNullOrWhiteSpace(cancelUrl) || string.IsNullOrWhiteSpace(notifUrl))
        {
            return new PaymentInitiationResult(
                false, null, PaymentStatus.Failed, null, null, null,
                "CONFIG", "URLs return/cancel/notif Orange manquantes.");
        }

        var client = CreateClient();
        var token = await GetTokenAsync(client, cancellationToken);
        var path = _options.Providers.OrangeDirect.WebPaymentPath.TrimStart('/');

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Content = JsonContent.Create(new
        {
            merchant_key = _secrets.MerchantKey,
            currency = request.Currency.ToUpperInvariant(),
            order_id = request.InternalReference,
            amount = (int)decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero),
            return_url = returnUrl,
            cancel_url = cancelUrl,
            notif_url = notifUrl,
            lang = "fr",
            reference = Truncate(request.Description, 50)
        }, options: JsonOpts);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Orange webpayment HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(body, 300));
            return new PaymentInitiationResult(
                false, null, PaymentStatus.Failed, null, null, null,
                "PROVIDER_ERROR", "Échec d'initiation Orange Money WebPay.");
        }

        var parsed = JsonSerializer.Deserialize<OrangeWebPayResponse>(body, JsonOpts);
        if (string.IsNullOrWhiteSpace(parsed?.PaymentUrl) || string.IsNullOrWhiteSpace(parsed.PayToken))
        {
            return new PaymentInitiationResult(
                false, null, PaymentStatus.Failed, null, null, null,
                "PROVIDER_ERROR", "Réponse Orange Money incomplète (payment_url / pay_token).");
        }

        return new PaymentInitiationResult(
            true,
            parsed.PayToken,
            PaymentStatus.PendingCustomerConfirmation,
            parsed.Status ?? "PENDING",
            "Vous allez être redirigé vers la page sécurisée Orange Money pour finaliser le paiement.",
            "#150*50#",
            null,
            null,
            PaymentUrl: parsed.PaymentUrl);
    }

    public async Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (IsLocal())
            throw new InvalidOperationException("Orange live ne doit pas être appelé en Environment=Local.");

        // Le statut définitif arrive surtout via notif_url ; pas d'endpoint status universel documenté ici.
        // On laisse l'orchestrateur s'appuyer sur le webhook / polling applicatif.
        _logger.LogDebug("Orange GetStatus non implémenté pour pay_token {Ref} — attendre notif webhook.", providerReference);
        return await Task.FromResult(new PaymentStatusResult(
            true,
            PaymentStatus.PendingCustomerConfirmation,
            "PENDING",
            null,
            "XAF",
            "ORANGE",
            null,
            null));
    }

    public Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(false, true, null, null, "Remboursement Orange WebPay non activé."));

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (IsLocal() || IsPlaceholder(_secrets.MerchantKey))
            return Task.FromResult(new WebhookValidationResult(true, null));
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

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var parsed = JsonSerializer.Deserialize<OrangeNotifPayload>(payload, JsonOpts);
        var raw = parsed?.Status ?? "UNKNOWN";
        decimal? amount = null;
        if (decimal.TryParse(parsed?.Amount?.ToString(), out var amt))
            amount = amt;

        return new MobileMoneyWebhookEventModel(
            parsed?.TxnId ?? parsed?.PayToken,
            "webpay.notif",
            parsed?.PayToken,
            parsed?.OrderId,
            MapStatus(raw),
            raw,
            amount,
            parsed?.Currency ?? "XAF",
            "ORANGE",
            hash,
            payload);
    }

    public Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Providers.OrangeDirect.Enabled)
            return Task.FromResult(new ProviderHealthResult(false, "Orange Money désactivé."));
        if (IsLocal())
            return Task.FromResult(new ProviderHealthResult(true, "Orange Local (simulateur)."));
        if (IsPlaceholder(_secrets.MerchantKey) ||
            (IsPlaceholder(_secrets.AuthorizationHeader) &&
             (IsPlaceholder(_secrets.ClientId) || IsPlaceholder(_secrets.ClientSecret))))
            return Task.FromResult(new ProviderHealthResult(false, "Secrets Orange manquants."));
        return Task.FromResult(new ProviderHealthResult(true, $"Orange {_options.Providers.OrangeDirect.Environment} configuré."));
    }

    private async Task<string> GetTokenAsync(HttpClient client, CancellationToken ct)
    {
        var cacheKey = "orange-oauth";
        if (_tokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            return cached.Token;

        var basic = ResolveBasicAuth();
        using var req = new HttpRequestMessage(HttpMethod.Post, "oauth/v3/token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await client.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OrangeTokenResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Réponse token Orange invalide.");
        if (string.IsNullOrWhiteSpace(body.AccessToken))
            throw new InvalidOperationException("Jeton Orange vide.");

        _tokenCache[cacheKey] = (body.AccessToken, DateTime.UtcNow.AddSeconds(Math.Max(60, body.ExpiresIn - 60)));
        return body.AccessToken;
    }

    private string ResolveBasicAuth()
    {
        if (!IsPlaceholder(_secrets.AuthorizationHeader))
        {
            var header = _secrets.AuthorizationHeader.Trim();
            return header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                ? header["Basic ".Length..].Trim()
                : header;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_secrets.ClientId}:{_secrets.ClientSecret}"));
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("OrangeMoney");
        var baseUrl = string.IsNullOrWhiteSpace(_options.Providers.OrangeDirect.BaseUrl)
            ? "https://api.orange.com"
            : _options.Providers.OrangeDirect.BaseUrl;
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(45);
        return client;
    }

    private string DefaultNotifUrl()
    {
        var pub = _configuration["Deployment:PublicBaseUrl"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(pub) ? "" : $"{pub}/api/webhooks/orange";
    }

    private void EnsureSecrets()
    {
        if (IsPlaceholder(_secrets.MerchantKey))
            throw new InvalidOperationException("MerchantKey Orange absent (secrets.json → MobileMoney:Orange).");
        if (IsPlaceholder(_secrets.AuthorizationHeader) &&
            (IsPlaceholder(_secrets.ClientId) || IsPlaceholder(_secrets.ClientSecret)))
            throw new InvalidOperationException("Credentials OAuth Orange absents (ClientId/Secret ou AuthorizationHeader).");
    }

    private bool IsLocal() =>
        _options.Providers.OrangeDirect.Environment.Equals("Local", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static PaymentStatus MapStatus(string raw) =>
        raw.Trim().ToUpperInvariant() switch
        {
            "SUCCESS" or "SUCCESSFUL" or "SUCCESSFULLY" or "COMPLETED" => PaymentStatus.Succeeded,
            "PENDING" or "INITIATED" => PaymentStatus.PendingCustomerConfirmation,
            "FAILED" or "EXPIRED" => PaymentStatus.Failed,
            "CANCELLED" or "CANCELED" => PaymentStatus.Cancelled,
            _ => PaymentStatus.RequiresReview
        };

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];

    private sealed class OrangeTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 3600;
    }

    private sealed class OrangeWebPayResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("pay_token")]
        public string? PayToken { get; set; }

        [JsonPropertyName("payment_url")]
        public string? PaymentUrl { get; set; }

        [JsonPropertyName("notif_token")]
        public string? NotifToken { get; set; }
    }

    private sealed class OrangeNotifPayload
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }

        [JsonPropertyName("txnid")]
        public string? TxnId { get; set; }

        [JsonPropertyName("amount")]
        public object? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("pay_token")]
        public string? PayToken { get; set; }

        [JsonPropertyName("notif_token")]
        public string? NotifToken { get; set; }
    }
}
