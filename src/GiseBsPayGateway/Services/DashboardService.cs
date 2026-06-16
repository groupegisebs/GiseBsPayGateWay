using GiseBsPayGateway.Data;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;

    public DashboardService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var successful = await _db.PaymentTransactions.CountAsync(x => x.Status == PaymentStatus.Succeeded, cancellationToken);
        var failed = await _db.PaymentTransactions.CountAsync(x => x.Status == PaymentStatus.Failed, cancellationToken);
        var pending = await _db.PaymentTransactions.CountAsync(x => x.Status == PaymentStatus.Pending, cancellationToken);
        var revenue = await _db.PaymentTransactions
            .Where(x => x.Status == PaymentStatus.Succeeded)
            .SumAsync(x => x.Amount, cancellationToken);
        var activeSubs = await _db.Subscriptions.CountAsync(x => x.Status == SubscriptionStatus.Active, cancellationToken);
        var apps = await _db.ClientApplications.CountAsync(cancellationToken);

        return new DashboardStats(revenue, successful, failed, activeSubs, apps, pending);
    }
}
