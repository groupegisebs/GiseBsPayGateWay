namespace GiseBsPayGateway.Options;

public class StripeSecretsOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Clés production (ou legacy si Live n'est pas renseigné).</summary>
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool IsLiveMode { get; set; }

    /// <summary>Clés production explicites (prioritaires sur les champs plats).</summary>
    public StripeKeySetOptions Live { get; set; } = new();

    /// <summary>Clés bac à sable — utilisées si la requête envoie X-Stripe-Env: DEV.</summary>
    public StripeKeySetOptions Test { get; set; } = new();
}

public class StripeKeySetOptions
{
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
