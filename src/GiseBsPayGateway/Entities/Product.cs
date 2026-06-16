namespace GiseBsPayGateway.Entities;

public class Product : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public string ProductCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? StripeProductId { get; set; }

    public ICollection<PricingPlan> PricingPlans { get; set; } = [];
}
