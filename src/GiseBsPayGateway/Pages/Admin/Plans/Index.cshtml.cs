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
        Plans = await _db.PricingPlans.AsNoTracking()
            .Include(x => x.Product)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Product.Name)
            .ThenByDescending(x => x.CreatedAt)
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
                return RedirectToPage();
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

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCopyAsync(Guid planId, CancellationToken cancellationToken)
    {
        var source = await _db.PricingPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken);

        if (source is null)
        {
            return NotFound();
        }

        var newPlanCode = await ResolveCopyPlanCodeAsync(source.ProductId, source.PlanCode, cancellationToken);

        var copy = new PricingPlan
        {
            ProductId = source.ProductId,
            PlanCode = newPlanCode,
            Name = newPlanCode == source.PlanCode ? source.Name : $"{source.Name} (copie)",
            Amount = source.Amount,
            Currency = source.Currency,
            BillingInterval = source.BillingInterval,
            IsActive = true
        };

        _db.PricingPlans.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PricingPlanCopied",
            nameof(PricingPlan),
            copy.Id.ToString(),
            true,
            $"From={source.PlanCode}; New={newPlanCode}",
            userName: User.Identity?.Name);

        TempData["PlanMessage"] = source.IsActive
            ? $"Copie créée et activée : « {newPlanCode} » (l'original « {source.PlanCode} » reste actif)."
            : $"Copie créée et activée : « {newPlanCode} » à partir du plan inactif « {source.PlanCode} ».";

        return RedirectToPage();
    }

    private async Task<string> ResolveCopyPlanCodeAsync(Guid productId, string planCode, CancellationToken cancellationToken)
    {
        if (!await _db.PricingPlans.AnyAsync(
                x => x.ProductId == productId && x.PlanCode == planCode && x.IsActive,
                cancellationToken))
        {
            return planCode;
        }

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{planCode}-V{i}";
            if (candidate.Length > 50)
            {
                candidate = candidate[..50];
            }

            if (!await _db.PricingPlans.AnyAsync(
                    x => x.ProductId == productId && x.PlanCode == candidate && x.IsActive,
                    cancellationToken))
            {
                return candidate;
            }
        }

        return $"{planCode}-COPY-{DateTime.UtcNow:yyyyMMddHHmm}"[..50];
    }
}
