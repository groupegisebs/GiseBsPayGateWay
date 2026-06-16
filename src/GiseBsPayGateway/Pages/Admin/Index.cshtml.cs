using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiseBsPayGateway.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly IDashboardService _dashboardService;

    public IndexModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public DashboardStats Stats { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Stats = await _dashboardService.GetStatsAsync(cancellationToken);
    }
}
