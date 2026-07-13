namespace GiseBsPayGateway.Options;

/// <summary>
/// Callbacks sortants vers les apps (ex. BoutiqueGise) pour les événements Connect/payout.
/// Si vide, utilise ClientApplication.WebhookCallbackUrl.
/// </summary>
public class PayoutCallbackOptions
{
    public const string SectionName = "PayoutCallbacks";

    /// <summary>Secret HMAC partagé pour signer les callbacks (header X-PayGateway-Signature).</summary>
    public string? SharedSecret { get; set; }
}
