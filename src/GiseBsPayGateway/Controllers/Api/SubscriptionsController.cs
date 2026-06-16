using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public SubscriptionsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("cancel")]
    public async Task<ActionResult<CancelSubscriptionResponse>> Cancel([FromBody] CancelSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var result = await _paymentService.CancelSubscriptionAsync(app, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }
}
