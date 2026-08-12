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

    public int StripeCount { get; private set; }

    public record WebhookViewModel(
        string Provider,
        DateTime CreatedAt,
        string EventId,
        string EventType,
        string? Reference,
        string ProcessingStatus,
        DateTime? ProcessedAt,
        string? ErrorMessage);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        StripeCount = await _db.StripeWebhookEvents.AsNoTracking().CountAsync(cancellationToken);

        var stripeQuery = _db.StripeWebhookEvents.AsNoTracking();
        if (search is not null)
        {
            stripeQuery = stripeQuery.Where(x =>
                EF.Functions.ILike(x.StripeEventId, $"%{search}%") ||
                EF.Functions.ILike(x.EventType, $"%{search}%") ||
                EF.Functions.ILike(x.ProcessingStatus.ToString(), $"%{search}%") ||
                (x.ErrorMessage != null && EF.Functions.ILike(x.ErrorMessage, $"%{search}%")));
        }

        var merged = await stripeQuery
            .Select(x => new WebhookViewModel(
                "stripe",
                x.CreatedAt,
                x.StripeEventId,
                x.EventType,
                null,
                x.ProcessingStatus.ToString(),
                x.ProcessedAt,
                x.ErrorMessage))
            .ToListAsync(cancellationToken);

        var ordered = merged.OrderByDescending(x => x.CreatedAt).ToList();
        var totalCount = ordered.Count;
        Pagination = AdminListPagination.Create(page, search, totalCount, null);
        PageNumber = Pagination.Page;
        Events = ordered.Skip(Pagination.Skip).Take(AdminListPagination.PageSize).ToList();
    }
}
