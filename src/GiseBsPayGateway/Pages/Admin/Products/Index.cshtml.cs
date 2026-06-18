using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Products;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICatalogService _catalogService;
    private readonly IAuditService _auditService;

    public IndexModel(ApplicationDbContext db, ICatalogService catalogService, IAuditService auditService)
    {
        _db = db;
        _catalogService = catalogService;
        _auditService = auditService;
    }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<ProductViewModel> Products { get; private set; } = [];

    public record ProductViewModel(
        Guid Id,
        string AppName,
        string ProductCode,
        string Name,
        string? StripeProductId,
        bool IsActive);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var (page, search) = AdminListPagination.Parse(PageNumber, Search);
        Search = search;

        IQueryable<Product> query = _db.Products.AsNoTracking()
            .Include(x => x.ClientApplication);

        if (search is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.ClientApplication.Name, $"%{search}%") ||
                EF.Functions.ILike(x.ProductCode, $"%{search}%") ||
                EF.Functions.ILike(x.Name, $"%{search}%") ||
                (x.StripeProductId != null && EF.Functions.ILike(x.StripeProductId, $"%{search}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Pagination = AdminListPagination.Create(page, search, totalCount);
        PageNumber = Pagination.Page;

        Products = await query
            .OrderBy(x => x.ClientApplication.AppCode)
            .ThenBy(x => x.ProductCode)
            .Skip(Pagination.Skip)
            .Take(AdminListPagination.PageSize)
            .Select(x => new ProductViewModel(
                x.Id,
                x.ClientApplication.Name,
                x.ProductCode,
                x.Name,
                x.StripeProductId,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSyncStripeAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.Id == productId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (!product.IsActive)
        {
            TempData["ProductError"] = $"Le produit « {product.ProductCode} » est inactif.";
            return RedirectToPage(new { page = PageNumber, search = Search });
        }

        try
        {
            var synced = await _catalogService.SyncProductToStripeAsync(
                product.ClientApplication, product.ProductCode, cancellationToken);

            await _auditService.LogAsync(
                "ProductSyncedToStripeAdmin", nameof(Product), product.Id.ToString(), true,
                product.ProductCode, userName: User.Identity?.Name);

            TempData["ProductMessage"] =
                $"« {product.ProductCode} » synchronisé avec Stripe (produit : {synced.StripeProductId}).";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ProductError"] = ex.Message;
        }

        return RedirectToPage(new { page = PageNumber, search = Search });
    }
}
