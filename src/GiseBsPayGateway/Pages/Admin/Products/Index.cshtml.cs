using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Products;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public AdminPaginationInfo Pagination { get; private set; } = null!;

    public IList<ProductViewModel> Products { get; private set; } = [];

    public record ProductViewModel(string AppName, string ProductCode, string Name, string? StripeProductId, bool IsActive);

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
                x.ClientApplication.Name,
                x.ProductCode,
                x.Name,
                x.StripeProductId,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }
}
