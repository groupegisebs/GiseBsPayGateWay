using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GiseBsPayGateway.Options;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services.Flutterwave;

public interface IFlutterwaveApiClient
{
    bool IsConfigured { get; }

    /// <summary>Flux v4 : customer → payment_method → charge.</summary>
    Task<FlutterwaveChargeResult> ChargeMobileMoneyAsync(FlutterwaveMobileMoneyChargeRequest request, CancellationToken ct = default);

    Task<FlutterwaveVerifyResult?> GetChargeAsync(string chargeId, CancellationToken ct = default);
}

public sealed class FlutterwaveApiClient : IFlutterwaveApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlutterwaveOptions _options;
    private readonly ILogger<FlutterwaveApiClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public FlutterwaveApiClient(
        HttpClient http,
        IHttpClientFactory httpClientFactory,
        IOptions<FlutterwaveOptions> options,
        ILogger<FlutterwaveApiClient> logger)
    {
        _http = http;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.ResolvedBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<FlutterwaveChargeResult> ChargeMobileMoneyAsync(
        FlutterwaveMobileMoneyChargeRequest request,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var (dial, national) = MobileMoneyNetworkCatalog.SplitPhone(request.PhoneNumber, request.PhoneCountryCode);
        var nameParts = SplitName(request.FullName);

        // Step 1 — Customer
        var customerId = await CreateCustomerAsync(request.Email, nameParts, dial, national, ct);

        // Step 2 — Payment method (mobile_money)
        var paymentMethodId = await CreateMobileMoneyPaymentMethodAsync(
            dial, request.Network, national, ct);

        // Step 3 — Charge
        var chargePayload = new Dictionary<string, object?>
        {
            ["currency"] = request.Currency.ToUpperInvariant(),
            ["customer_id"] = customerId,
            ["payment_method_id"] = paymentMethodId,
            ["amount"] = request.Amount,
            ["reference"] = request.Reference
        };

        using var chargeReq = await CreateAuthorizedRequestAsync(HttpMethod.Post, "charges", ct);
        chargeReq.Headers.TryAddWithoutValidation("X-Idempotency-Key", request.Reference);
        if (!string.IsNullOrWhiteSpace(request.ScenarioKey))
            chargeReq.Headers.TryAddWithoutValidation("X-Scenario-Key", request.ScenarioKey);

        chargeReq.Content = JsonContent.Create(chargePayload, options: JsonOpts);
        using var chargeResp = await _http.SendAsync(chargeReq, ct);
        var body = await chargeResp.Content.ReadAsStringAsync(ct);

        _logger.LogInformation(
            "Flutterwave v4 charge reference={Reference} HTTP {Status}",
            request.Reference,
            (int)chargeResp.StatusCode);

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        if (!chargeResp.IsSuccessStatusCode)
        {
            var err = ExtractError(root) ?? $"Flutterwave charge échouée (HTTP {(int)chargeResp.StatusCode}).";
            throw new InvalidOperationException(err);
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Réponse Flutterwave charge invalide (data manquant).");

        var chargeId = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var status = data.TryGetProperty("status", out var st) ? st.GetString() : null;
        string? instruction = null;
        string? redirectUrl = null;

        if (data.TryGetProperty("next_action", out var next) && next.ValueKind == JsonValueKind.Object)
        {
            var nextType = next.TryGetProperty("type", out var nt) ? nt.GetString() : null;
            if (string.Equals(nextType, "payment_instruction", StringComparison.OrdinalIgnoreCase)
                && next.TryGetProperty("payment_instruction", out var pi)
                && pi.ValueKind == JsonValueKind.Object
                && pi.TryGetProperty("note", out var note))
            {
                instruction = note.GetString();
            }
            else if (string.Equals(nextType, "redirect_url", StringComparison.OrdinalIgnoreCase)
                     && next.TryGetProperty("redirect_url", out var ru)
                     && ru.ValueKind == JsonValueKind.Object
                     && ru.TryGetProperty("url", out var url))
            {
                redirectUrl = url.GetString();
                instruction = "Complétez l'autorisation sur la page Flutterwave.";
            }
        }

        instruction ??= "Validez le paiement sur votre téléphone (notification mobile money / PIN).";

        return new FlutterwaveChargeResult(
            Success: true,
            Message: root.TryGetProperty("message", out var msg) ? msg.GetString() : "Charge created",
            FlutterwaveStatus: status,
            FlutterwaveChargeId: chargeId,
            FlutterwaveCustomerId: customerId,
            FlutterwavePaymentMethodId: paymentMethodId,
            Instruction: instruction,
            RedirectUrl: redirectUrl,
            RawJson: body);
    }

    public async Task<FlutterwaveVerifyResult?> GetChargeAsync(string chargeId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(chargeId))
            return null;

        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"charges/{Uri.EscapeDataString(chargeId)}", ct);
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Flutterwave get charge HTTP {Status}: {Body}", (int)response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;

        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;
        decimal? amount = null;
        if (data.TryGetProperty("amount", out var amt) && amt.TryGetDecimal(out var a))
            amount = a;
        var currency = data.TryGetProperty("currency", out var cur) ? cur.GetString() : null;

        return new FlutterwaveVerifyResult(status, id, reference, amount, currency, body);
    }

    private async Task<string> CreateCustomerAsync(
        string email,
        (string First, string? Middle, string Last) name,
        string dial,
        string national,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["name"] = new Dictionary<string, object?>
            {
                ["first"] = name.First,
                ["middle"] = name.Middle,
                ["last"] = name.Last
            },
            ["phone"] = new Dictionary<string, object?>
            {
                ["country_code"] = dial,
                ["number"] = national
            }
        };

        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, "customers", ct);
        req.Headers.TryAddWithoutValidation("X-Idempotency-Key", $"cust-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(payload, options: JsonOpts);

        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractError(doc.RootElement) ?? "Création client Flutterwave échouée.");

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("id", out var idEl)
            || string.IsNullOrWhiteSpace(idEl.GetString()))
            throw new InvalidOperationException("Flutterwave customer id manquant.");

        return idEl.GetString()!;
    }

    private async Task<string> CreateMobileMoneyPaymentMethodAsync(
        string dial,
        string network,
        string national,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "mobile_money",
            ["mobile_money"] = new Dictionary<string, object?>
            {
                ["country_code"] = dial,
                ["network"] = network,
                ["phone_number"] = national
            }
        };

        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, "payment-methods", ct);
        req.Headers.TryAddWithoutValidation("X-Idempotency-Key", $"pmd-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(payload, options: JsonOpts);

        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractError(doc.RootElement) ?? "Création payment method Flutterwave échouée.");

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("id", out var idEl)
            || string.IsNullOrWhiteSpace(idEl.GetString()))
            throw new InvalidOperationException("Flutterwave payment_method id manquant.");

        return idEl.GetString()!;
    }

    private async Task EnsureAccessTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return;

            using var tokenClient = _httpClientFactory.CreateClient("FlutterwaveToken");
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId.Trim(),
                ["client_secret"] = _options.ClientSecret.Trim(),
                ["grant_type"] = "client_credentials"
            });

            using var response = await tokenClient.PostAsync(_options.TokenUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Flutterwave OAuth échoué (HTTP {(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var sec)
                ? sec
                : 600;

            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Flutterwave OAuth: access_token manquant.");

            _accessToken = token;
            // Renouveler 30s avant expiration
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30));
            _logger.LogInformation("Flutterwave OAuth token obtenu (expire dans {Seconds}s)", expiresIn);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string relativePath, CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        var req = new HttpRequestMessage(method, relativePath.TrimStart('/'));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.TryAddWithoutValidation("X-Trace-Id", Guid.NewGuid().ToString("N"));
        return req;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Flutterwave v4 non configuré (Flutterwave:ClientId / Flutterwave:ClientSecret).");
    }

    private static string? ExtractError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
        {
            if (err.TryGetProperty("message", out var m))
                return m.GetString();
        }

        if (root.TryGetProperty("message", out var msg))
            return msg.GetString();
        return null;
    }

    private static (string First, string? Middle, string Last) SplitName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return ("Customer", null, "User");

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => (parts[0], null, parts[0]),
            2 => (parts[0], null, parts[1]),
            _ => (parts[0], string.Join(' ', parts[1..^1]), parts[^1])
        };
    }
}

public sealed record FlutterwaveMobileMoneyChargeRequest(
    string Reference,
    decimal Amount,
    string Currency,
    string Email,
    string PhoneNumber,
    string PhoneCountryCode,
    string Network,
    string? FullName,
    string? ScenarioKey = null);

public sealed record FlutterwaveChargeResult(
    bool Success,
    string? Message,
    string? FlutterwaveStatus,
    string? FlutterwaveChargeId,
    string? FlutterwaveCustomerId,
    string? FlutterwavePaymentMethodId,
    string? Instruction,
    string? RedirectUrl,
    string RawJson);

public sealed record FlutterwaveVerifyResult(
    string? Status,
    string? ChargeId,
    string? Reference,
    decimal? Amount,
    string? Currency,
    string RawJson);
