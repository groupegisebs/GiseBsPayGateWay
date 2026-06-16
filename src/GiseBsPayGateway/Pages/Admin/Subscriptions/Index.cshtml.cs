using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Subscriptions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public IList<SubscriptionViewModel> Subscriptions { get; private set; } = [];

    public record SubscriptionViewModel(string SubscriptionCode, string AppName, string CustomerCode, string PlanCode, string Status, string Period, string? StripeSubscriptionId);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Subscriptions = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.PricingPlan)
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .Select(x => new SubscriptionViewModel(
                x.SubscriptionCode,
                x.ClientApplication.Name,
                x.Customer.CustomerCode,
                x.PricingPlan.PlanCode,
                x.Status.ToString(),
                x.CurrentPeriodStart.HasValue && x.CurrentPeriodEnd.HasValue
                    ? $"{x.CurrentPeriodStart:g} → {x.CurrentPeriodEnd:g}"
                    : "-",
                x.StripeSubscriptionId))
            .ToListAsync(cancellationToken);
    }
}
