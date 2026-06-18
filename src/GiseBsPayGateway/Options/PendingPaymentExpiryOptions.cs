namespace GiseBsPayGateway.Options;

public class PendingPaymentExpiryOptions
{
    public const string SectionName = "PendingPaymentExpiry";

    public bool Enabled { get; set; } = true;

    /// <summary>Pending payments older than this are candidates for cancellation.</summary>
    public int ExpiryHours { get; set; } = 24;

    /// <summary>How often the background job runs.</summary>
    public int IntervalMinutes { get; set; } = 60;

    public int BatchSize { get; set; } = 100;
}
