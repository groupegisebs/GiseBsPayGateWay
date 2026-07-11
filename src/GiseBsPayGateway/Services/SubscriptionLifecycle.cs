using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services;

/// <summary>
/// Règle métier : un abonnement annulé reste accessible jusqu'à CurrentPeriodEnd,
/// puis passe à Cancelled à cette date.
/// </summary>
public static class SubscriptionLifecycle
{
    public static SubscriptionStatus GetEffectiveStatus(Subscription subscription, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var periodEnd = subscription.CurrentPeriodEnd;

        if (periodEnd.HasValue && periodEnd.Value > now)
        {
            // Période payée encore en cours : rester actif même si canceled / fin programmée.
            if (subscription.Status == SubscriptionStatus.Cancelled
                || subscription.CancelAtPeriodEnd
                || subscription.CancelledAt.HasValue)
            {
                return subscription.Status == SubscriptionStatus.Trialing
                    ? SubscriptionStatus.Trialing
                    : SubscriptionStatus.Active;
            }

            return subscription.Status;
        }

        // Période terminée + annulation demandée → Cancelled
        if (subscription.CancelAtPeriodEnd
            || subscription.Status == SubscriptionStatus.Cancelled
            || subscription.CancelledAt.HasValue)
        {
            return SubscriptionStatus.Cancelled;
        }

        return subscription.Status;
    }

    public static bool IsScheduledToEnd(Subscription subscription, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        return (subscription.CancelAtPeriodEnd
                || subscription.Status == SubscriptionStatus.Cancelled
                || subscription.CancelledAt.HasValue)
               && subscription.CurrentPeriodEnd.HasValue
               && subscription.CurrentPeriodEnd.Value > now;
    }

    /// <summary>
    /// Applique le statut Stripe en respectant la règle « actif jusqu'à fin de période ».
    /// </summary>
    public static void ApplyStripeStatus(
        Subscription subscription,
        string? stripeStatus,
        bool cancelAtPeriodEnd,
        DateTime? stripeCanceledAt,
        DateTime? periodEnd,
        DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        subscription.CancelAtPeriodEnd = cancelAtPeriodEnd;
        if (periodEnd.HasValue)
            subscription.CurrentPeriodEnd = periodEnd;

        if (stripeCanceledAt.HasValue)
            subscription.CancelledAt = stripeCanceledAt;

        var canceledOnStripe = string.Equals(stripeStatus, "canceled", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(stripeStatus, "incomplete_expired", StringComparison.OrdinalIgnoreCase);

        if (canceledOnStripe || cancelAtPeriodEnd)
        {
            subscription.CancelAtPeriodEnd = true;
            subscription.CancelledAt ??= stripeCanceledAt ?? now;

            if (periodEnd.HasValue && periodEnd.Value > now)
            {
                subscription.Status = SubscriptionStatus.Active;
            }
            else
            {
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.CancelAtPeriodEnd = false;
            }

            return;
        }

        subscription.Status = stripeStatus switch
        {
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "unpaid" => SubscriptionStatus.Unpaid,
            "trialing" => SubscriptionStatus.Trialing,
            "incomplete" => SubscriptionStatus.Incomplete,
            _ => subscription.Status
        };
    }

    /// <summary>Normalise le statut local si la période est terminée.</summary>
    public static bool NormalizeIfPeriodEnded(Subscription subscription, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        if (!subscription.CurrentPeriodEnd.HasValue || subscription.CurrentPeriodEnd.Value > now)
            return false;

        if (subscription.Status == SubscriptionStatus.Cancelled && !subscription.CancelAtPeriodEnd)
            return false;

        if (!subscription.CancelAtPeriodEnd
            && subscription.Status != SubscriptionStatus.Cancelled
            && !subscription.CancelledAt.HasValue)
        {
            return false;
        }

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelAtPeriodEnd = false;
        subscription.CancelledAt ??= subscription.CurrentPeriodEnd;
        subscription.UpdatedAt = now;
        return true;
    }
}
