using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

public class PricingPlan : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string PlanCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "eur";
    public decimal Amount { get; set; }
    public BillingInterval BillingInterval { get; set; }
    public bool IsActive { get; set; } = true;
    public string? StripePriceId { get; set; }

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
