using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Webhooks;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public IList<WebhookViewModel> Events { get; private set; } = [];

    public record WebhookViewModel(DateTime CreatedAt, string StripeEventId, string EventType, string ProcessingStatus, DateTime? ProcessedAt, string? ErrorMessage);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Events = await _db.StripeWebhookEvents.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
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
