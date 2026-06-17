namespace GiseBsPayGateway.Entities;

public class CollectedTaxRecord : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public Guid? PaymentTransactionId { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }

    public string PaymentCode { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;

    public DateTime CollectedAt { get; set; }

    public string? BillingLine1 { get; set; }
    public string? BillingLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }

    public decimal AmountSubtotal { get; set; }
    public decimal TaxAmountTotal { get; set; }
    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = "eur";

    public string? StripeTaxTransactionId { get; set; }

    public ICollection<CollectedTaxLine> Lines { get; set; } = [];
}
