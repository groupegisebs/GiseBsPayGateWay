using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

public class PaymentTransaction : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid PricingPlanId { get; set; }
    public PricingPlan PricingPlan { get; set; } = null!;

    public string PaymentCode { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "eur";

    public string? StripeCheckoutSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public string? StripeInvoiceId { get; set; }
    public string? StripeBalanceTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? PaidAt { get; set; }

    public decimal? AmountSubtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? StripeFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? BillingCountry { get; set; }
    public string? BillingState { get; set; }

    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
}
