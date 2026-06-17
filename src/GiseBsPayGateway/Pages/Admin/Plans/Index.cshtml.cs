using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Plans;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;

    public IndexModel(ApplicationDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<PlanViewModel> Plans { get; private set; } = [];

    public record PlanViewModel(
        Guid Id,
        string ProductName,
        string PlanCode,
        string Name,
        decimal Amount,
        string Currency,
        string BillingInterval,
        string? StripePriceId,
        bool IsActive,
        DateTime CreatedAt);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        IQueryable<PricingPlan> query = _db.PricingPlans.AsNoTracking()
            .Include(x => x.Product);

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Product.Name, $"%{search}%") ||
                EF.Functions.ILike(x.PlanCode, $"%{search}%") ||
                EF.Functions.ILike(x.Name, $"%{search}%") ||
                (x.StripePriceId != null && EF.Functions.ILike(x.StripePriceId, $"%{search}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Plans = await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Product.Name)
            .ThenByDescending(x => x.CreatedAt)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new PlanViewModel(
                x.Id,
                x.Product.Name,
                x.PlanCode,
                x.Name,
                x.Amount,
                x.Currency,
                x.BillingInterval.ToString(),
                x.StripePriceId,
                x.IsActive,
                x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await _db.PricingPlans
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken);

        if (plan is null)
        {
            return NotFound();
        }

        if (!plan.IsActive)
        {
            var duplicateActive = await _db.PricingPlans.AnyAsync(
                x => x.ProductId == plan.ProductId &&
                     x.PlanCode == plan.PlanCode &&
                     x.IsActive &&
                     x.Id != plan.Id,
                cancellationToken);

            if (duplicateActive)
            {
                TempData["PlanError"] = $"Impossible d'activer « {plan.PlanCode} » : un autre plan actif utilise déjà ce code pour ce produit.";
                return RedirectToPage(new { page = PageNumber, search = Search });
            }
        }

        plan.IsActive = !plan.IsActive;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var action = plan.IsActive ? "PricingPlanActivated" : "PricingPlanDeactivated";
        await _auditService.LogAsync(action, nameof(PricingPlan), plan.Id.ToString(), true, plan.PlanCode, userName: User.Identity?.Name);

        TempData["PlanMessage"] = plan.IsActive
            ? $"Plan « {plan.PlanCode} » activé."
            : $"Plan « {plan.PlanCode} » désactivé. Créez un nouveau plan si le tarif change.";

        return RedirectToPage(new { page = PageNumber, search = Search });
    }
}
