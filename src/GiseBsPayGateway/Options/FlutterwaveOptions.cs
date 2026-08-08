namespace GiseBsPayGateway.Options;

/// <summary>Configuration Flutterwave API v4 (OAuth + Mobile Money).</summary>
public class FlutterwaveOptions
{
    public const string SectionName = "Flutterwave";

    /// <summary>Client ID (dashboard Flutterwave → API Keys v4).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client Secret (dashboard Flutterwave → API Keys v4).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Endpoint OAuth2 client_credentials.</summary>
    public string TokenUrl { get; set; } =
        "https://idp.flutterwave.com/realms/flutterwave/protocol/openid-connect/token";

    /// <summary>
    /// Base API v4.
    /// Sandbox : https://developersandbox-api.flutterwave.com
    /// Production : https://api.flutterwave.cloud (ou URL live du dashboard)
    /// </summary>
    public string BaseUrl { get; set; } = "https://developersandbox-api.flutterwave.com";

    /// <summary>true = sandbox developersandbox-api ; false = BaseUrl tel quel.</summary>
    public bool UseSandbox { get; set; } = true;

    /// <summary>Secret webhook (vérification signature / verif-hash selon config dashboard).</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Alias legacy — préférer WebhookSecret.</summary>
    public string? WebhookHash
    {
        get => WebhookSecret;
        set => WebhookSecret = value;
    }

    /// <summary>
    /// Montants locaux fixes pour <b>10 USD</b> (clés = XOF, XAF, GHS…).
    /// Surcharge la grille par défaut ; utile dans secrets.json.
    /// </summary>
    public Dictionary<string, decimal> FixedAmountsFor10Usd { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public string ResolvedBaseUrl =>
        UseSandbox
            ? "https://developersandbox-api.flutterwave.com"
            : (string.IsNullOrWhiteSpace(BaseUrl)
                ? "https://api.flutterwave.cloud"
                : BaseUrl.TrimEnd('/'));
}
