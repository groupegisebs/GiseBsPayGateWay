using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Webhooks;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<WebhookViewModel> Events { get; private set; } = [];

    public record WebhookViewModel(DateTime CreatedAt, string StripeEventId, string EventType, string ProcessingStatus, DateTime? ProcessedAt, string? ErrorMessage);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        var query = _db.StripeWebhookEvents.AsNoTracking();

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.StripeEventId, $"%{search}%") ||
                EF.Functions.ILike(x.EventType, $"%{search}%") ||
                EF.Functions.ILike(x.ProcessingStatus.ToString(), $"%{search}%") ||
                (x.ErrorMessage != null && EF.Functions.ILike(x.ErrorMessage, $"%{search}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Events = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new WebhookViewModel(
                x.CreatedAt,
                x.StripeEventId,
                x.EventType,
                x.ProcessingStatus.ToString(),
                x.ProcessedAt,
                x.ErrorMessage))
            .ToListAsync(cancellationToken);
    }
}
