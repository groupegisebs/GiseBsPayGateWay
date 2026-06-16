namespace GiseBsPayGateway.Entities;

public class StripeSettings : BaseEntity
{
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool IsLiveMode { get; set; }
    public bool IsActive { get; set; } = true;
}
