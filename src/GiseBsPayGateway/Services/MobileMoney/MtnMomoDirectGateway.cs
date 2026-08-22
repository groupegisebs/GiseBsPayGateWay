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
/// MTN MoMo Collections (Request to Pay) — Cameroun.
/// Sandbox : https://sandbox.momodeveloper.mtn.com
/// Prod : https://proxy.momoapi.mtn.com — X-Target-Environment: mtncameroon
/// </summary>
public sealed class MtnMomoDirectGateway : IMobileMoneyGateway
{
    public const string Code = "mtn_direct";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MobileMoneyOptions _options;
    private readonly MtnSecretsOptions _secrets;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MtnMomoDirectGateway> _logger;
    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> _tokenCache = new();
    private string? _workingSubscriptionKey;

    public MtnMomoDirectGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<MobileMoneyOptions> options,
        IOptions<MtnSecretsOptions> secrets,
        IConfiguration configuration,
        ILogger<MtnMomoDirectGateway> logger)
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
            throw new InvalidOperationException("MTN live ne doit pas être appelé en Environment=Local (utiliser le simulateur).");

        EnsureSecrets();

        if (!request.Currency.Equals("XAF", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentInitiationResult(
                false, null, PaymentStatus.Failed, null, null, null,
                "INVALID_CURRENCY", "MTN Collections Cameroun n'accepte que la devise ISO XAF.");
        }

        var referenceId = MtnMoMoOpenApi.ToReferenceId(request.IdempotencyKey, request.InternalReference);
        var client = CreateClient();
        var token = await GetTokenAsync(client, cancellationToken);
        var target = ResolveTargetEnvironment();
        var msisdn = NormalizeMsisdn(request.PhoneNumber);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "collection/v1_0/requesttopay");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", ActiveSubscriptionKey());
        httpRequest.Headers.TryAddWithoutValidation("X-Reference-Id", referenceId);
        httpRequest.Headers.TryAddWithoutValidation("X-Target-Environment", target);
        var callback = ResolveCallbackUrl();
        if (!string.IsNullOrWhiteSpace(callback))
            httpRequest.Headers.TryAddWithoutValidation("X-Callback-Url", callback);

        var note = MtnMoMoOpenApi.SanitizeNote(request.Description);
        httpRequest.Content = JsonContent.Create(new
        {
            amount = ((int)decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero)).ToString(),
            currency = "XAF",
            externalId = request.InternalReference,
            payer = new { partyIdType = "MSISDN", partyId = msisdn },
            payerMessage = note,
            payeeNote = note
        }, options: JsonOpts);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Accepted
            or System.Net.HttpStatusCode.Created
            or System.Net.HttpStatusCode.OK)
        {
            return PendingResult(referenceId);
        }

        var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = MtnMoMoOpenApi.FromHttp(response.StatusCode, errBody);
        if (error.IsDuplicate)
        {
            _logger.LogInformation("MTN requesttopay déjà accepté pour {ReferenceId} — lecture du statut.", referenceId);
            return await StatusAsInitiationAsync(referenceId, cancellationToken);
        }

        _logger.LogWarning("MTN requesttopay HTTP {Status} {Code}: {Body}",
            (int)response.StatusCode, error.Code, Truncate(errBody, 300));
        return new PaymentInitiationResult(
            false, null, error.Status, error.Code, null, null, error.Code, error.UserMessage);
    }

    public async Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (IsLocal())
            throw new InvalidOperationException("MTN live ne doit pas être appelé en Environment=Local.");

        EnsureSecrets();
        var client = CreateClient();
        var token = await GetTokenAsync(client, cancellationToken);
        var target = ResolveTargetEnvironment();

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"collection/v1_0/requesttopay/{Uri.EscapeDataString(providerReference)}");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", ActiveSubscriptionKey());
        httpRequest.Headers.TryAddWithoutValidation("X-Target-Environment", target);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = MtnMoMoOpenApi.FromHttp(response.StatusCode, body);
            return new PaymentStatusResult(
                error.KeepPending,
                error.Status,
                error.Code,
                null,
                null,
                "MTN",
                error.Code,
                error.UserMessage);
        }

        var parsed = JsonSerializer.Deserialize<MtnStatusResponse>(body, JsonOpts);
        var raw = parsed?.Status ?? "UNKNOWN";
        var mapped = MapStatus(raw);
        if (mapped is PaymentStatus.Failed or PaymentStatus.Expired
            && !string.IsNullOrWhiteSpace(parsed?.Reason))
        {
            var reason = MtnMoMoOpenApi.FromCode(parsed.Reason);
            return new PaymentStatusResult(
                true, reason.Status, parsed.Reason, decimal.TryParse(parsed.Amount, out var failedAmt) ? failedAmt : null,
                parsed.Currency, "MTN", reason.Code, reason.UserMessage);
        }

        return new PaymentStatusResult(
            true,
            mapped,
            raw,
            decimal.TryParse(parsed?.Amount, out var amt) ? amt : null,
            parsed?.Currency,
            "MTN",
            null,
            null);
    }

    public Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(false, true, null, null, "Remboursement MTN Collections non activé."));

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        // Callback Open API : PUT une seule fois, sans retry. Le polling GET /requesttopay/{id} reste obligatoire.
        if (IsLocal() || IsPlaceholder(ResolveSubscriptionKey()))
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
        var parsed = JsonSerializer.Deserialize<MtnCallbackPayload>(payload, JsonOpts);
        var raw = parsed?.Status ?? "UNKNOWN";
        decimal? amount = null;
        if (decimal.TryParse(parsed?.Amount, out var amt))
            amount = amt;

        // externalId = notre PaymentCode ; financialTransactionId / reference = id MTN
        var providerRef = FirstNonEmpty(
            request.Headers.TryGetValue("X-Reference-Id", out var xref) ? xref.ToString() : null,
            parsed?.ReferenceId,
            parsed?.FinancialTransactionId);

        return new MobileMoneyWebhookEventModel(
            parsed?.FinancialTransactionId ?? providerRef,
            "requesttopay.status",
            providerRef,
            parsed?.ExternalId,
            MapStatus(raw),
            raw,
            amount,
            parsed?.Currency,
            "MTN",
            hash,
            payload);
    }

    public Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Providers.MtnDirect.Enabled)
            return Task.FromResult(new ProviderHealthResult(false, "MTN MoMo désactivé."));
        if (IsLocal())
            return Task.FromResult(new ProviderHealthResult(true, "MTN Local (simulateur)."));
        if (IsPlaceholder(ResolveSubscriptionKey()) || IsPlaceholder(_secrets.ApiUserId) || IsPlaceholder(_secrets.ApiKey))
            return Task.FromResult(new ProviderHealthResult(false, "Secrets MTN manquants."));
        return Task.FromResult(new ProviderHealthResult(true, $"MTN {_options.Providers.MtnDirect.Environment} configuré."));
    }

    private async Task<string> GetTokenAsync(HttpClient client, CancellationToken ct)
    {
        var cacheKey = ResolveBaseUrl();
        if (_tokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            return cached.Token;

        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_secrets.ApiUserId}:{_secrets.ApiKey}"));

        Exception? last = null;
        foreach (var subscriptionKey in SubscriptionKeys())
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "collection/token/");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscriptionKey);
            // Open API : /token n'accepte pas de body (400 BAD REQUEST sinon).

            using var response = await client.SendAsync(req, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                last = new InvalidOperationException("Clé d'abonnement MTN refusée (401). Essai de la Secondary Key.");
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                var mapped = MtnMoMoOpenApi.FromHttp(response.StatusCode, err);
                last = new InvalidOperationException(mapped.UserMessage);
                continue;
            }

            var body = await response.Content.ReadFromJsonAsync<MtnTokenResponse>(JsonOpts, ct)
                ?? throw new InvalidOperationException("Réponse token MTN invalide.");
            if (string.IsNullOrWhiteSpace(body.AccessToken))
                throw new InvalidOperationException("Jeton MTN vide.");

            _workingSubscriptionKey = subscriptionKey;
            _tokenCache[cacheKey] = (body.AccessToken, DateTime.UtcNow.AddSeconds(Math.Max(60, body.ExpiresIn - 60)));
            return body.AccessToken;
        }

        throw last ?? new InvalidOperationException("Impossible d'obtenir un jeton MTN.");
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("MtnMoMo");
        client.BaseAddress = new Uri(ResolveBaseUrl().TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(45);
        return client;
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.Providers.MtnDirect.BaseUrl))
            return _options.Providers.MtnDirect.BaseUrl;
        return IsProduction()
            ? "https://proxy.momoapi.mtn.com"
            : "https://sandbox.momodeveloper.mtn.com";
    }

    private string ResolveTargetEnvironment()
    {
        if (!string.IsNullOrWhiteSpace(_options.Providers.MtnDirect.TargetEnvironment))
            return _options.Providers.MtnDirect.TargetEnvironment;
        return IsProduction() ? "mtncameroon" : "sandbox";
    }

    private string ResolveCallbackUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.Providers.MtnDirect.CallbackUrl))
            return _options.Providers.MtnDirect.CallbackUrl.Trim();
        var pub = _configuration["Deployment:PublicBaseUrl"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(pub) ? "" : $"{pub}/api/webhooks/mtn";
    }

    private void EnsureSecrets()
    {
        if (IsPlaceholder(ResolveSubscriptionKey()) ||
            IsPlaceholder(_secrets.ApiUserId) ||
            IsPlaceholder(_secrets.ApiKey))
            throw new InvalidOperationException("Credentials MTN MoMo absents (secrets.json → MobileMoney:Mtn).");
    }

    private string ResolveSubscriptionKey() => _secrets.ResolveSubscriptionKey();

    private string ActiveSubscriptionKey() =>
        !string.IsNullOrWhiteSpace(_workingSubscriptionKey)
            ? _workingSubscriptionKey
            : ResolveSubscriptionKey();

    private IEnumerable<string> SubscriptionKeys()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { _secrets.SubscriptionKey, _secrets.PrimaryKey, _secrets.SecondaryKey })
        {
            if (IsPlaceholder(key) || !seen.Add(key.Trim()))
                continue;
            yield return key.Trim();
        }
    }

    private static PaymentInitiationResult PendingResult(string referenceId) =>
        new(
            true,
            referenceId,
            PaymentStatus.PendingCustomerConfirmation,
            "PENDING",
            "Consultez votre téléphone MTN et confirmez la demande de paiement. Ne communiquez jamais votre code secret.",
            "*126#",
            null,
            null);

    private async Task<PaymentInitiationResult> StatusAsInitiationAsync(string referenceId, CancellationToken ct)
    {
        var status = await GetStatusAsync(referenceId, ct);
        if (!status.Success)
            return PendingResult(referenceId);

        return new PaymentInitiationResult(
            true,
            referenceId,
            status.NormalizedStatus,
            status.RawProviderStatus,
            status.NormalizedStatus == PaymentStatus.PendingCustomerConfirmation
                ? "Consultez votre téléphone MTN et confirmez la demande de paiement. Ne communiquez jamais votre code secret."
                : null,
            "*126#",
            status.FailureCode,
            status.FailureMessage);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private bool IsLocal() =>
        _options.Providers.MtnDirect.Environment.Equals("Local", StringComparison.OrdinalIgnoreCase);

    private bool IsProduction() =>
        _options.Providers.MtnDirect.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMsisdn(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("237") && digits.Length >= 12)
            return digits;
        if (digits.Length == 9)
            return "237" + digits;
        return digits;
    }

    private static PaymentStatus MapStatus(string raw) =>
        raw.Trim().ToUpperInvariant() switch
        {
            "SUCCESSFUL" or "SUCCESS" or "SUCCESSFULLY" => PaymentStatus.Succeeded,
            "PENDING" or "ONGOING" => PaymentStatus.PendingCustomerConfirmation,
            "FAILED" or "REJECTED" => PaymentStatus.Failed,
            "TIMEOUT" or "EXPIRED" => PaymentStatus.Expired,
            "CANCELLED" or "CANCELED" => PaymentStatus.Cancelled,
            _ => PaymentStatus.RequiresReview
        };

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];

    private sealed class MtnTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 3600;
    }

    private sealed class MtnStatusResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("externalId")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("financialTransactionId")]
        public string? FinancialTransactionId { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    private sealed class MtnCallbackPayload
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("externalId")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("financialTransactionId")]
        public string? FinancialTransactionId { get; set; }

        [JsonPropertyName("referenceId")]
        public string? ReferenceId { get; set; }
    }
}
