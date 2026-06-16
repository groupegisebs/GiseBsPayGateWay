using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Plans;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;

    public CreateModel(ApplicationDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SelectList Products { get; private set; } = null!;

    public bool IsCopyMode { get; private set; }

    public string? CopySourcePlanCode { get; private set; }

    public class InputModel
    {
        [Required, Display(Name = "Produit")]
        public Guid ProductId { get; set; }

        [Required, MaxLength(50), Display(Name = "Code plan")]
        public string PlanCode { get; set; } = string.Empty;

        [Required, MaxLength(200), Display(Name = "Nom")]
        public string Name { get; set; } = string.Empty;

        [Required, Range(0.01, 999999), Display(Name = "Montant")]
        public decimal Amount { get; set; }

        [Required, MaxLength(3), Display(Name = "Devise")]
        public string Currency { get; set; } = "eur";

        [Required, Display(Name = "Intervalle")]
        public BillingInterval BillingInterval { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid? copyFrom, CancellationToken cancellationToken)
    {
        Products = await LoadProductsAsync(cancellationToken);

        if (copyFrom is null)
        {
            return Page();
        }

        var source = await _db.PricingPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == copyFrom.Value, cancellationToken);

        if (source is null)
        {
            return NotFound();
        }

        IsCopyMode = true;
        CopySourcePlanCode = source.PlanCode;

        var suggestedPlanCode = await ResolveCopyPlanCodeAsync(source.ProductId, source.PlanCode, cancellationToken);

        Input = new InputModel
        {
            ProductId = source.ProductId,
            PlanCode = suggestedPlanCode,
            Name = suggestedPlanCode == source.PlanCode ? source.Name : $"{source.Name} (copie)",
            Amount = source.Amount,
            Currency = source.Currency,
            BillingInterval = source.BillingInterval
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Products = await LoadProductsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var planCode = Input.PlanCode.ToUpperInvariant();
        if (await _db.PricingPlans.AnyAsync(
                x => x.ProductId == Input.ProductId && x.PlanCode == planCode && x.IsActive,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(Input.PlanCode),
                "Un plan actif avec ce code existe déjà pour ce produit. Désactivez-le avant d'en créer un nouveau avec le même code.");
            return Page();
        }

        var plan = new PricingPlan
        {
            ProductId = Input.ProductId,
            PlanCode = planCode,
            Name = Input.Name,
            Amount = Input.Amount,
            Currency = Input.Currency.ToLowerInvariant(),
            BillingInterval = Input.BillingInterval,
            IsActive = true
        };

        _db.PricingPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("PricingPlanCreated", nameof(PricingPlan), plan.Id.ToString(), true, plan.PlanCode, userName: User.Identity?.Name);
        return RedirectToPage("Index");
    }

    private async Task<SelectList> LoadProductsAsync(CancellationToken cancellationToken)
    {
        var products = await _db.Products.Include(x => x.ClientApplication)
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return new SelectList(products, nameof(Product.Id), nameof(Product.Name));
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
