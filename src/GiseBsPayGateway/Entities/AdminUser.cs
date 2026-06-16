using Microsoft.AspNetCore.Identity;

namespace GiseBsPayGateway.Entities;

public class AdminUser : IdentityUser
{
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
