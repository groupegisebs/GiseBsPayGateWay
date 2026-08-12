using GiseBsPayGateway.Authentication;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services.MobileMoney;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/mobile-money")]
public class MobileMoneyController : ControllerBase
{
    private readonly IMobileMoneyOrchestrator _orchestrator;

    public MobileMoneyController(IMobileMoneyOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("charge")]
    public async Task<ActionResult<MobileMoneyChargeResponse>> Charge(
        [FromBody] MobileMoneyChargeRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var key)
            ? key.ToString()
            : null;

        try
        {
            var result = await _orchestrator.ChargeAsync(app, request, idempotencyKey, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("{paymentCode}/status")]
    public async Task<ActionResult<MobileMoneyStatusResponse>> Status(
        string paymentCode,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var result = await _orchestrator.RefreshStatusAsync(app, paymentCode, cancellationToken);
        if (result is null)
            return NotFound(new ApiErrorResponse("Paiement introuvable.", null));
        return Ok(result);
    }
}
