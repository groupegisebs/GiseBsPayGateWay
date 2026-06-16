using GiseBsPayGateway.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Transactions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public IList<TransactionViewModel> Transactions { get; private set; } = [];

    public record TransactionViewModel(DateTime CreatedAt, string PaymentCode, string AppName, string CustomerCode, string ProductCode, decimal Amount, string Currency, string Status, DateTime? PaidAt);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Transactions = await _db.PaymentTransactions.AsNoTracking()
            .Include(x => x.ClientApplication)
            .Include(x => x.Customer)
            .Include(x => x.Product)
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .Select(x => new TransactionViewModel(
                x.CreatedAt,
                x.PaymentCode,
                x.ClientApplication.Name,
                x.Customer.CustomerCode,
                x.Product.ProductCode,
                x.Amount,
                x.Currency,
                x.Status.ToString(),
                x.PaidAt))
            .ToListAsync(cancellationToken);
    }
}
