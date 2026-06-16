namespace GiseBsPayGateway.Options;

public class StripeSecretsOptions
{
    public const string SectionName = "Stripe";

    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool IsLiveMode { get; set; }
}
