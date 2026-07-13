namespace GiseBsPayGateway.Entities;

public enum DisbursementStatus
{
    PendingReview = 0,
    NeedsReconciliation = 1,
    Approved = 2,
    Queued = 3,
    Submitted = 4,
    Paid = 5,
    Failed = 6,
    Rejected = 7,
    Held = 8
}

public enum DisbursementChannel
{
    StripeConnect = 0,
    PayPal = 1,
    MobileMoney = 2,
    Manual = 9
}

/// <summary>
/// Demande de décaissement soumise par une app (ex. BoutiqueGise).
/// Doit être vérifiée / rapprochée dans l'admin PayGateway avant paiement.
/// </summary>
public class SellerDisbursementRequest : BaseEntity
{
    public int? ClientApplicationIdLegacy { get; set; }
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }

    public string ExternalReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SellerExternalId { get; set; } = string.Empty;
    public string? SellerDisplayName { get; set; }

    public DisbursementChannel Channel { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string DestinationMasked { get; set; } = string.Empty;
    /// <summary>Token / acct / email / phone token — usage interne admin uniquement.</summary>
    public string? DestinationToken { get; set; }

    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "cad";
    public string CountryCode { get; set; } = "CA";

    public DisbursementStatus Status { get; set; } = DisbursementStatus.PendingReview;
    public string? ReconciliationNotes { get; set; }
    public bool ReconciliationChecked { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }

    public string? ProviderPayoutId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? MetadataJson { get; set; }
}

/// <summary>Compte PayPal lié via OAuth (tokens côté serveur uniquement).</summary>
public class PayPalLinkedAccount : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public string? PayerId { get; set; }
    public string? MaskedEmail { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public string? AccessTokenEncrypted { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
    public string Status { get; set; } = "linked";
    public DateTime? LastVerifiedAt { get; set; }
}

/// <summary>
/// Destinataire Mobile Money — infos publiques uniquement (pas de PIN/OTP).
/// </summary>
public class MobileMoneyRecipient : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string OperatorCode { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string PhoneE164 { get; set; } = string.Empty;
    public string MaskedPhone { get; set; } = string.Empty;
    public string? PublicAccountId { get; set; }
    public string Status { get; set; } = "registered";
}
