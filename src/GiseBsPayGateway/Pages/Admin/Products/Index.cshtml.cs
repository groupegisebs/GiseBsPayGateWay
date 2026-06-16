using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Products;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public IList<ProductViewModel> Products { get; private set; } = [];

    public record ProductViewModel(string AppName, string ProductCode, string Name, string? StripeProductId, bool IsActive);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Products = await _db.Products.AsNoTracking()
            .Include(x => x.ClientApplication)
            .OrderBy(x => x.ClientApplication.AppCode)
            .ThenBy(x => x.ProductCode)
            .Select(x => new ProductViewModel(
                x.ClientApplication.Name,
                x.ProductCode,
                x.Name,
                x.StripeProductId,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }
}
