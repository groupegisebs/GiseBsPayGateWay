using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Products;

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

    public SelectList Applications { get; private set; } = null!;

    public class InputModel
    {
        [Required, Display(Name = "Application")]
        public Guid ClientApplicationId { get; set; }

        [Required, MaxLength(50), Display(Name = "Code produit")]
        public string ProductCode { get; set; } = string.Empty;

        [Required, MaxLength(200), Display(Name = "Nom")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Description")]
        public string? Description { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Applications = new SelectList(
            await _db.ClientApplications.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken),
            nameof(ClientApplication.Id),
            nameof(ClientApplication.Name));
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Applications = new SelectList(
            await _db.ClientApplications.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken),
            nameof(ClientApplication.Id),
            nameof(ClientApplication.Name));

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var product = new Product
        {
            ClientApplicationId = Input.ClientApplicationId,
            ProductCode = Input.ProductCode.ToUpperInvariant(),
            Name = Input.Name,
            Description = Input.Description,
            IsActive = true
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("ProductCreated", nameof(Product), product.Id.ToString(), true, product.ProductCode, userName: User.Identity?.Name);
        return RedirectToPage("Index");
    }
}
