using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
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

    public SelectList PlanCodes { get; private set; } = null!;

    public SelectList Currencies { get; private set; } = null!;

    public bool IsCopyMode { get; private set; }

    public string? CopySourcePlanCode { get; private set; }

    public class InputModel
    {
        [Required, Display(Name = "Produit")]
        public Guid ProductId { get; set; }

        [Required, Display(Name = "Code plan")]
        public string PlanCode { get; set; } = CatalogOptions.Monthly;

        [Required, MaxLength(200), Display(Name = "Nom")]
        public string Name { get; set; } = string.Empty;

        [Required, Range(0.01, 999999), Display(Name = "Montant")]
        public decimal Amount { get; set; }

        [Required, Display(Name = "Devise")]
        public string Currency { get; set; } = "usd";
    }

    public async Task<IActionResult> OnGetAsync(Guid? copyFrom, CancellationToken cancellationToken)
    {
        await LoadSelectListsAsync(cancellationToken);

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
            PlanCode = CatalogOptions.TryGetPlanCode(suggestedPlanCode, out var option)
                ? option.Code
                : CatalogOptions.Monthly,
            Name = suggestedPlanCode == source.PlanCode ? source.Name : $"{source.Name} (copie)",
            Amount = source.Amount,
            Currency = CatalogOptions.TryGetCurrency(source.Currency, out var currency) ? currency : "usd"
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadSelectListsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        CatalogOptions.PlanCodeOption planOption;
        try
        {
            planOption = CatalogOptions.ResolvePlanCode(Input.PlanCode);
            Input.Currency = CatalogOptions.ResolveCurrency(Input.Currency);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        if (await _db.PricingPlans.AnyAsync(
                x => x.ProductId == Input.ProductId && x.PlanCode == planOption.Code && x.IsActive,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(Input.PlanCode),
                "Un plan actif avec ce code existe déjà pour ce produit. Désactivez-le avant d'en créer un nouveau avec le même code.");
            return Page();
        }

        var plan = new PricingPlan
        {
            ProductId = Input.ProductId,
            PlanCode = planOption.Code,
            Name = Input.Name,
            Amount = Input.Amount,
            Currency = Input.Currency,
            BillingInterval = planOption.BillingInterval,
            IsActive = true
        };

        _db.PricingPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("PricingPlanCreated", nameof(PricingPlan), plan.Id.ToString(), true, plan.PlanCode, userName: User.Identity?.Name);
        return RedirectToPage("Index");
    }

    private async Task LoadSelectListsAsync(CancellationToken cancellationToken)
    {
        Products = await LoadProductsAsync(cancellationToken);
        PlanCodes = new SelectList(
            CatalogOptions.PlanCodes.Select(x => new { x.Code, Label = $"{x.Code} — {x.Label}" }),
            "Code",
            "Label",
            Input.PlanCode);
        Currencies = new SelectList(
            CatalogOptions.Currencies.Select(x => new { x.Code, x.Label }),
            "Code",
            "Label",
            Input.Currency);
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
        if (CatalogOptions.TryGetPlanCode(planCode, out var option) &&
            !await _db.PricingPlans.AnyAsync(
                x => x.ProductId == productId && x.PlanCode == option.Code && x.IsActive,
                cancellationToken))
        {
            return option.Code;
        }

        foreach (var candidate in CatalogOptions.PlanCodes.Select(x => x.Code))
        {
            if (!await _db.PricingPlans.AnyAsync(
                    x => x.ProductId == productId && x.PlanCode == candidate && x.IsActive,
                    cancellationToken))
            {
                return candidate;
            }
        }

        return CatalogOptions.Monthly;
    }
}
