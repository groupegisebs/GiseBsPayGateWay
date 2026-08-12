using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

/// <summary>Événements webhook Mobile Money (CamPay / futurs Orange &amp; MTN) — anti-rejeu.</summary>
public class MobileMoneyWebhookEvent : BaseEntity
{
    public string Provider { get; set; } = "campay";
    public string? ProviderEventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Received;
    public string? Payload { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProcessingResult { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
