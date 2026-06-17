using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Subscriptions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<SubscriptionViewModel> Subscriptions { get; private set; } = [];

    public record SubscriptionViewModel(string SubscriptionCode, string AppName, string CustomerCode, string PlanCode, string Status, string Period, string? StripeSubscriptionId);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        IQueryable<Subscription> query = _db.Subscriptions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.PricingPlan);

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.SubscriptionCode, $"%{search}%") ||
                EF.Functions.ILike(x.ClientApplication.Name, $"%{search}%") ||
                EF.Functions.ILike(x.Customer.CustomerCode, $"%{search}%") ||
                EF.Functions.ILike(x.PricingPlan.PlanCode, $"%{search}%") ||
                EF.Functions.ILike(x.Status.ToString(), $"%{search}%") ||
                (x.StripeSubscriptionId != null && EF.Functions.ILike(x.StripeSubscriptionId, $"%{search}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Subscriptions = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
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
