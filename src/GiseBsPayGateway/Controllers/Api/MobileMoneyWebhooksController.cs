using GiseBsPayGateway.Services.MobileMoney;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/webhooks")]
public class MobileMoneyWebhooksController : ControllerBase
{
    private readonly IMobileMoneyOrchestrator _orchestrator;

    public MobileMoneyWebhooksController(IMobileMoneyOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("campay")]
    [RequestSizeLimit(64 * 1024)]
    public Task<IActionResult> CamPay(CancellationToken cancellationToken) =>
        HandleAsync("campay", cancellationToken);

    [HttpPost("orange")]
    [RequestSizeLimit(64 * 1024)]
    public Task<IActionResult> Orange(CancellationToken cancellationToken) =>
        HandleAsync("orange", cancellationToken);

    [HttpPost("mtn")]
    [RequestSizeLimit(64 * 1024)]
    public Task<IActionResult> Mtn(CancellationToken cancellationToken) =>
        HandleAsync("mtn", cancellationToken);

    private async Task<IActionResult> HandleAsync(string provider, CancellationToken cancellationToken)
    {
        var (status, body) = await _orchestrator.HandleWebhookAsync(provider, Request, cancellationToken);
        return StatusCode(status, body);
    }
}
