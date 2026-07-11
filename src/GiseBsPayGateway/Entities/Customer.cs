namespace GiseBsPayGateway.Entities;

public class Customer : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public string CustomerCode { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? ExternalUserId { get; set; }
    public string? StripeCustomerId { get; set; }

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
