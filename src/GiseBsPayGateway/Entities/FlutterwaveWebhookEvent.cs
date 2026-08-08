using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

/// <summary>Journal des webhooks Flutterwave (Mobile Money), miroir de <see cref="StripeWebhookEvent"/>.</summary>
public class FlutterwaveWebhookEvent : BaseEntity
{
    /// <summary>Clé d'idempotence (chargeId:status:type ou hash du payload).</summary>
    public string FlutterwaveEventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? ChargeId { get; set; }
    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Received;
    public string? Payload { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
