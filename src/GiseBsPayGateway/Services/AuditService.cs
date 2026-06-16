using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;

namespace GiseBsPayGateway.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(string action, string entityType, string? entityId, bool isSuccess, string? details = null, string? appCode = null, string? userId = null, string? userName = null, string? ipAddress = null)
    {
        var context = _httpContextAccessor.HttpContext;
        var log = new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            IsSuccess = isSuccess,
            Details = details,
            AppCode = appCode,
            UserId = userId,
            UserName = userName,
            IpAddress = ipAddress ?? context?.Connection.RemoteIpAddress?.ToString()
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
