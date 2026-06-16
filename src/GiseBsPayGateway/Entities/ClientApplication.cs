namespace GiseBsPayGateway.Entities;

public class ClientApplication : BaseEntity
{
    public string AppCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? AllowedDomains { get; set; }
    public string? WebhookCallbackUrl { get; set; }

    public ICollection<ApplicationApiKey> ApiKeys { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
    public ICollection<Product> Products { get; set; } = [];
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
