namespace GiseBsPayGateway.Options;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public bool Enabled { get; set; }
    public bool UseSandbox { get; set; } = true;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>URL publique du callback OAuth PayGateway, ex. https://pay.example.com/api/paypal/oauth/callback</summary>
    public string? OAuthRedirectUri { get; set; }
    /// <summary>Clé locale pour chiffrer refresh tokens (32+ chars). Sinon dérivée du ClientSecret.</summary>
    public string? TokenEncryptionKey { get; set; }

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    public string ApiBaseUrl => UseSandbox
        ? "https://api-m.sandbox.paypal.com"
        : "https://api-m.paypal.com";

    public string AuthBaseUrl => UseSandbox
        ? "https://www.sandbox.paypal.com"
        : "https://www.paypal.com";
}
