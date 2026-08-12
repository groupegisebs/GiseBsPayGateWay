namespace GiseBsPayGateway.Options;

public class MobileMoneyOptions
{
    public const string SectionName = "MobileMoney";

    public string DefaultProvider { get; set; } = "CamPay";
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
    public bool Enabled { get; set; } = true;
    /// <summary>Local | Sandbox | Production</summary>
    public string Environment { get; set; } = "Local";
    /// <summary>Vide = dérivé de Environment (demo.campay.net / campay.net).</summary>
    public string BaseUrl { get; set; } = "";
}

public class OrangeDirectProviderOptions
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Sandbox";
    public string BaseUrl { get; set; } = "";
    public string MerchantCode { get; set; } = "";
    public string SecretReference { get; set; } = "";
}

public class MtnDirectProviderOptions
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Sandbox";
    public string BaseUrl { get; set; } = "";
    public string SecretReference { get; set; } = "";
}

/// <summary>Secrets CamPay injectés via secrets.json (jamais dans appsettings commités).</summary>
public class CamPaySecretsOptions
{
    public const string SectionName = "MobileMoney:CamPay";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
