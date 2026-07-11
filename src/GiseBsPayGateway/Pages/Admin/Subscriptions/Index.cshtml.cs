using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Subscriptions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ISubscriptionSyncService _syncService;

    public IndexModel(ApplicationDbContext db, ISubscriptionSyncService syncService)
    {
        _db = db;
        _syncService = syncService;
    }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<SubscriptionViewModel> Subscriptions { get; private set; } = [];

    public record SubscriptionViewModel(
        Guid Id,
        string SubscriptionCode,
        string AppName,
        string CustomerCode,
        string ProductName,
        string PlanCode,
        decimal Amount,
        string Currency,
        bool AmountFromStripe,
        string Status,
        string Period,
        string? StripeSubscriptionId,
        bool CancelAtPeriodEnd,
        DateTime? StripeSyncedAt);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRefreshOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _syncService.RefreshFromStripeAsync(id, cancellationToken);
        TempData[result.Failed > 0 ? "Error" : "Success"] = result.Message;
        return RedirectToPage(new { page = PageNumber, search = Search });
    }

    public async Task<IActionResult> OnPostRefreshPageAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        var ids = Subscriptions.Select(x => x.Id).ToList();
        if (ids.Count == 0)
        {
            TempData["Error"] = "Aucun abonnement à synchroniser.";
            return RedirectToPage(new { page = PageNumber, search = Search });
        }

        var result = await _syncService.RefreshManyFromStripeAsync(ids, cancellationToken);
        TempData[result.Failed > 0 ? "Error" : "Success"] = result.Message;
        return RedirectToPage(new { page = PageNumber, search = Search });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        IQueryable<Subscription> query = _db.Subscriptions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .Include(x => x.PricingPlan);

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.SubscriptionCode, $"%{search}%") ||
                EF.Functions.ILike(x.ClientApplication.Name, $"%{search}%") ||
                EF.Functions.ILike(x.Customer.CustomerCode, $"%{search}%") ||
                EF.Functions.ILike(x.Product.Name, $"%{search}%") ||
                EF.Functions.ILike(x.Product.ProductCode, $"%{search}%") ||
                EF.Functions.ILike(x.PricingPlan.PlanCode, $"%{search}%") ||
                EF.Functions.ILike(x.PricingPlan.Currency, $"%{search}%") ||
                (x.StripeCurrency != null && EF.Functions.ILike(x.StripeCurrency, $"%{search}%")) ||
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
                x.Id,
                x.SubscriptionCode,
                x.ClientApplication.Name,
                x.Customer.CustomerCode,
                x.Product.Name,
                x.PricingPlan.PlanCode,
                x.StripeAmount ?? x.PricingPlan.Amount,
                x.StripeCurrency ?? x.PricingPlan.Currency,
                x.StripeAmount.HasValue,
                x.Status.ToString(),
                x.CurrentPeriodStart.HasValue && x.CurrentPeriodEnd.HasValue
                    ? $"{x.CurrentPeriodStart:g} → {x.CurrentPeriodEnd:g}"
                    : "-",
                x.StripeSubscriptionId,
                x.CancelAtPeriodEnd,
                x.StripeSyncedAt))
            .ToListAsync(cancellationToken);
    }
}
