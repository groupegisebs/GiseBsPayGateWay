namespace GiseBsPayGateway.Entities;

/// <summary>Compte Stripe Connect lié à une application cliente.</summary>
public class ConnectedAccount : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }

    public string ExternalReference { get; set; } = string.Empty;
    public string StripeAccountId { get; set; } = string.Empty;
    public string Country { get; set; } = "CA";
    public string Currency { get; set; } = "cad";
    public string? Email { get; set; }
    public string AccountType { get; set; } = "express";
    public bool ChargesEnabled { get; set; }
    public bool PayoutsEnabled { get; set; }
    public bool DetailsSubmitted { get; set; }
    public string Status { get; set; } = "pending";
    public string? RequirementsCurrentlyDueJson { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
