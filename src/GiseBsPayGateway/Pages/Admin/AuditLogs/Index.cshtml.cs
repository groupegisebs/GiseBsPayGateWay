using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.AuditLogs;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<AuditViewModel> Logs { get; private set; } = [];

    public record AuditViewModel(DateTime CreatedAt, string Action, string EntityType, string? UserName, string? AppCode, string? IpAddress, bool IsSuccess, string? Details);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        IQueryable<AuditLog> query = _db.AuditLogs.AsNoTracking();

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Action, $"%{search}%") ||
                EF.Functions.ILike(x.EntityType, $"%{search}%") ||
                (x.UserName != null && EF.Functions.ILike(x.UserName, $"%{search}%")) ||
                (x.AppCode != null && EF.Functions.ILike(x.AppCode, $"%{search}%")) ||
                (x.IpAddress != null && EF.Functions.ILike(x.IpAddress, $"%{search}%")) ||
                (x.Details != null && EF.Functions.ILike(x.Details, $"%{search}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Logs = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
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
