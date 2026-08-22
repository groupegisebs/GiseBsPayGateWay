namespace GiseBsPayGateway.Options;

public class MobileMoneyOptions
{
    public const string SectionName = "MobileMoney";

    /// <summary>Direct (défaut) | CamPay (legacy agrégateur).</summary>
    public string DefaultProvider { get; set; } = "Direct";
    public string Country { get; set; } = "CM";
    public string Currency { get; set; } = "XAF";
    public int ChargeExpiryMinutes { get; set; } = 15;
    public MobileMoneyProvidersOptions Providers { get; set; } = new();
}

public class MobileMoneyProvidersOptions
{
    public CamPayProviderOptions CamPay { get; set; } = new();
    public OrangeDirectProviderOptions OrangeDirect { get; set; } = new();
    public MtnDirectProviderOptions MtnDirect { get; set; } = new();
}

public class CamPayProviderOptions
{
    public bool Enabled { get; set; }
    /// <summary>Local | Sandbox | Production</summary>
    public string Environment { get; set; } = "Local";
    public string BaseUrl { get; set; } = "";
}

public class OrangeDirectProviderOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Local | Sandbox | Production</summary>
    public string Environment { get; set; } = "Local";
    /// <summary>Vide = api.orange.com</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>Chemin WebPay Cameroun.</summary>
    public string WebPaymentPath { get; set; } = "orange-money-webpay/cm/v1/webpayment";
    /// <summary>URLs de retour navigateur (surchargeables par la requête charge).</summary>
    public string ReturnUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";
    /// <summary>Vide = {Deployment:PublicBaseUrl}/api/webhooks/orange</summary>
    public string NotifUrl { get; set; } = "";
}

public class MtnDirectProviderOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Local | Sandbox | Production</summary>
    public string Environment { get; set; } = "Local";
    /// <summary>Vide = dérivé de Environment (sandbox.momodeveloper / proxy.momoapi).</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>X-Target-Environment : sandbox | mtncameroon</summary>
    public string TargetEnvironment { get; set; } = "";
    /// <summary>Callback HTTPS (X-Callback-Url). Vide = {PublicBaseUrl}/api/webhooks/mtn</summary>
    public string CallbackUrl { get; set; } = "";
}

/// <summary>Secrets CamPay (legacy) — secrets.json.</summary>
public class CamPaySecretsOptions
{
    public const string SectionName = "MobileMoney:CamPay";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}

/// <summary>Secrets Orange Money WebPay CM — secrets.json.</summary>
public class OrangeSecretsOptions
{
    public const string SectionName = "MobileMoney:Orange";

    /// <summary>Client id application Orange Developer (ou vide si AuthorizationHeader fourni).</summary>
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Valeur « Authorization header » déjà en Base64 (Basic …) depuis la console Orange.</summary>
    public string AuthorizationHeader { get; set; } = "";
    public string MerchantKey { get; set; } = "";
}

/// <summary>
/// Secrets MTN MoMo Collections (produit « Collections » du portail momodeveloper).
/// SubscriptionKey = Primary Key (Ocp-Apim-Subscription-Key). SecondaryKey = clé de secours du même produit.
/// ApiUserId / ApiKey = identifiants OAuth (sandbox : Provisioning API ; prod : Partner Portal).
/// </summary>
public class MtnSecretsOptions
{
    public const string SectionName = "MobileMoney:Mtn";

    public string SubscriptionKey { get; set; } = "";
    /// <summary>Primary Key du portail — alias de SubscriptionKey si celui-ci est vide.</summary>
    public string PrimaryKey { get; set; } = "";
    /// <summary>Secondary Key du même produit Collections.</summary>
    public string SecondaryKey { get; set; } = "";
    public string ApiUserId { get; set; } = "";
    public string ApiKey { get; set; } = "";

    public string ResolveSubscriptionKey()
    {
        if (!IsBlank(SubscriptionKey)) return SubscriptionKey.Trim();
        if (!IsBlank(PrimaryKey)) return PrimaryKey.Trim();
        if (!IsBlank(SecondaryKey)) return SecondaryKey.Trim();
        return "";
    }

    private static bool IsBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
}
