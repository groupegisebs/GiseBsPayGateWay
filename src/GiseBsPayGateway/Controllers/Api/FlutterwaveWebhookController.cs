using GiseBsPayGateway.Services.Flutterwave;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/webhooks/flutterwave")]
public class FlutterwaveWebhookController(
    IFlutterwaveMobileMoneyService mobileMoney,
    ILogger<FlutterwaveWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var hash = Request.Headers["verif-hash"].FirstOrDefault();

        try
        {
            await mobileMoney.HandleWebhookAsync(hash, raw, ct);
            return Ok(new { received = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Flutterwave webhook rejeté");
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Flutterwave webhook error");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Webhook processing failed." });
        }
    }
}
