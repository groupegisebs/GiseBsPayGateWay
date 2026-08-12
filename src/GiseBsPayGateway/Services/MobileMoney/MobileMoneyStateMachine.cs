using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

/// <summary>Transitions d'état contrôlées (BR-009).</summary>
public static class MobileMoneyStateMachine
{
    private static readonly HashSet<(PaymentStatus From, PaymentStatus To)> Allowed =
    [
        (PaymentStatus.Pending, PaymentStatus.PendingCustomerConfirmation),
        (PaymentStatus.Pending, PaymentStatus.Failed),
        (PaymentStatus.Pending, PaymentStatus.Cancelled),
        (PaymentStatus.PendingCustomerConfirmation, PaymentStatus.Processing),
        (PaymentStatus.PendingCustomerConfirmation, PaymentStatus.Succeeded),
        (PaymentStatus.PendingCustomerConfirmation, PaymentStatus.Failed),
        (PaymentStatus.PendingCustomerConfirmation, PaymentStatus.Expired),
        (PaymentStatus.PendingCustomerConfirmation, PaymentStatus.Cancelled),
        (PaymentStatus.PendingCustomerConfirmation, PaymentStatus.RequiresReview),
        (PaymentStatus.Processing, PaymentStatus.Succeeded),
        (PaymentStatus.Processing, PaymentStatus.Failed),
        (PaymentStatus.Processing, PaymentStatus.RequiresReview),
        (PaymentStatus.Succeeded, PaymentStatus.RefundPending),
        (PaymentStatus.RefundPending, PaymentStatus.Refunded),
        (PaymentStatus.RefundPending, PaymentStatus.PartiallyRefunded),
        (PaymentStatus.PartiallyRefunded, PaymentStatus.RefundPending),
        (PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded),
        (PaymentStatus.RequiresReview, PaymentStatus.Succeeded),
        (PaymentStatus.RequiresReview, PaymentStatus.Failed),
        (PaymentStatus.RequiresReview, PaymentStatus.Cancelled),
        (PaymentStatus.RequiresReview, PaymentStatus.Expired),
    ];

    public static bool CanTransition(PaymentStatus from, PaymentStatus to)
    {
        if (from == to)
            return true;

        // Succeeded is financially terminal for payment direction (no back to pending/failed).
        if (from == PaymentStatus.Succeeded &&
            to is PaymentStatus.Pending or PaymentStatus.PendingCustomerConfirmation
                or PaymentStatus.Processing or PaymentStatus.Failed or PaymentStatus.Expired
                or PaymentStatus.Cancelled)
            return false;

        return Allowed.Contains((from, to));
    }

    public static bool TryTransition(PaymentStatus from, PaymentStatus to, out PaymentStatus result)
    {
        if (!CanTransition(from, to))
        {
            result = from;
            return false;
        }

        result = to;
        return true;
    }
}
