namespace GiseBsPayGateway.Entities;

public class ApplicationApiKey : BaseEntity
{
    public Guid ClientApplicationId { get; set; }
    public ClientApplication ClientApplication { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
