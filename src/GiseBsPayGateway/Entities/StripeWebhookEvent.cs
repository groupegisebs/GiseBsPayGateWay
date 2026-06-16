using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

public class StripeWebhookEvent : BaseEntity
{
    public string StripeEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Received;
    public string? Payload { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
