namespace GiseBsPayGateway.Entities;

/// <summary>Transfert Stripe Connect vers un compte connecté.</summary>
public class ConnectTransfer : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    public string StripeTransferId { get; set; } = string.Empty;
    public string DestinationAccountId { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "cad";
    public string Status { get; set; } = "pending";
    public string? Description { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? MetadataJson { get; set; }
}
