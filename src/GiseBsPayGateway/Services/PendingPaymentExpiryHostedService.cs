using GiseBsPayGateway.Options;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public class PendingPaymentExpiryHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly PendingPaymentExpiryOptions _options;
    private readonly ILogger<PendingPaymentExpiryHostedService> _logger;

    public PendingPaymentExpiryHostedService(
        IServiceProvider services,
        IOptions<PendingPaymentExpiryOptions> options,
        ILogger<PendingPaymentExpiryHostedService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Pending payment expiry job is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        _logger.LogInformation(
            "Pending payment expiry job started (every {IntervalMinutes} min, after {ExpiryHours} h)",
            _options.IntervalMinutes,
            _options.ExpiryHours);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPendingPaymentExpiryService>();
                var result = await service.ExpireAbandonedAsync(stoppingToken);

                if (result.Cancelled > 0 || result.Reconciled > 0)
                {
                    _logger.LogInformation(
                        "Pending payment expiry: {Cancelled} cancelled, {Reconciled} reconciled",
                        result.Cancelled,
                        result.Reconciled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending payment expiry job failed");
            }
        }
    }
}
