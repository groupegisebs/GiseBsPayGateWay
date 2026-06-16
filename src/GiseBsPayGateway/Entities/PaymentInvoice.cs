using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Entities;

public class PaymentInvoice : BaseEntity
{
    public string InvoiceCode { get; set; } = string.Empty;

    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid PricingPlanId { get; set; }
    public PricingPlan PricingPlan { get; set; } = null!;

    public Guid? PaymentTransactionId { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }

    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerName { get; set; }

    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "eur";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;

    public string? BillingReason { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }

    public string? StripeInvoiceId { get; set; }
    public string? StripeInvoiceNumber { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public string? StripeCheckoutSessionId { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public string? InvoicePdfUrl { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? LineItemsDescription { get; set; }
}
