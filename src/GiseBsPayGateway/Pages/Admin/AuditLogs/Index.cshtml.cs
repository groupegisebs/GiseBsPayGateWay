using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.AuditLogs;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public IList<AuditViewModel> Logs { get; private set; } = [];

    public record AuditViewModel(DateTime CreatedAt, string Action, string EntityType, string? UserName, string? AppCode, string? IpAddress, bool IsSuccess, string? Details);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Logs = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(300)
            .Select(x => new AuditViewModel(
                x.CreatedAt,
                x.Action,
                x.EntityType,
                x.UserName,
                x.AppCode,
                x.IpAddress,
                x.IsSuccess,
                x.Details))
            .ToListAsync(cancellationToken);
    }
}
