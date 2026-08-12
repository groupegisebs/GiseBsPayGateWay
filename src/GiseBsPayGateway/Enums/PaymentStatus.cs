namespace GiseBsPayGateway.Enums;

public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Refunded = 5,
    /// <summary>Demande envoyée ; confirmation attendue sur le téléphone (Mobile Money).</summary>
    PendingCustomerConfirmation = 6,
    /// <summary>Délai de confirmation dépassé.</summary>
    Expired = 7,
    /// <summary>Remboursement demandé, résultat attendu.</summary>
    RefundPending = 8,
    /// <summary>Remboursement partiel.</summary>
    PartiallyRefunded = 9,
    /// <summary>Événement incohérent nécessitant une revue manuelle.</summary>
    RequiresReview = 10
}
