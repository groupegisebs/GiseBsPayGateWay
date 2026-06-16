using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Plans;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public IList<PlanViewModel> Plans { get; private set; } = [];

    public record PlanViewModel(string ProductName, string PlanCode, string Name, decimal Amount, string Currency, string BillingInterval, string? StripePriceId, bool IsActive);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Plans = await _db.PricingPlans.AsNoTracking()
            .Include(x => x.Product)
            .OrderBy(x => x.Product.Name)
            .ThenBy(x => x.PlanCode)
            .Select(x => new PlanViewModel(
                x.Product.Name,
                x.PlanCode,
                x.Name,
                x.Amount,
                x.Currency,
                x.BillingInterval.ToString(),
                x.StripePriceId,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }
}
