using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

public class Subscription : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid PricingPlanId { get; set; }
    public PricingPlan PricingPlan { get; set; } = null!;

    public string SubscriptionCode { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Incomplete;
    public string? StripeSubscriptionId { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
}
