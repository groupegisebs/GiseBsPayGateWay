using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;

namespace GiseBsPayGateway.Tests.Services;

public class SubscriptionLifecycleTests
{
    [Fact]
    public void GetEffectiveStatus_AnnuleMaisPeriodeEnCours_ResteActive()
    {
        var sub = new Subscription
        {
            Status = SubscriptionStatus.Cancelled,
            CancelAtPeriodEnd = false,
            CancelledAt = DateTime.UtcNow.AddDays(-1),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10)
        };

        Assert.Equal(SubscriptionStatus.Active, SubscriptionLifecycle.GetEffectiveStatus(sub));
        Assert.True(SubscriptionLifecycle.IsScheduledToEnd(sub));
    }

    [Fact]
    public void GetEffectiveStatus_PeriodeTerminee_DevientCancelled()
    {
        var sub = new Subscription
        {
            Status = SubscriptionStatus.Active,
            CancelAtPeriodEnd = true,
            CancelledAt = DateTime.UtcNow.AddDays(-20),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1)
        };

        Assert.Equal(SubscriptionStatus.Cancelled, SubscriptionLifecycle.GetEffectiveStatus(sub));
    }

    [Fact]
    public void ApplyStripeStatus_CanceledAvecPeriodeFuture_GardeActive()
    {
        var sub = new Subscription();
        var periodEnd = DateTime.UtcNow.AddDays(12);

        SubscriptionLifecycle.ApplyStripeStatus(
            sub,
            "canceled",
            cancelAtPeriodEnd: false,
            stripeCanceledAt: DateTime.UtcNow,
            periodEnd: periodEnd);

        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.True(sub.CancelAtPeriodEnd);
        Assert.Equal(periodEnd, sub.CurrentPeriodEnd);
    }
}
