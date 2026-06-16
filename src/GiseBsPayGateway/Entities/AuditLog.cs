namespace GiseBsPayGateway.Entities;

public class AuditLog : BaseEntity
{
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? AppCode { get; set; }
    public string? IpAddress { get; set; }
    public string? Details { get; set; }
    public bool IsSuccess { get; set; } = true;
}
