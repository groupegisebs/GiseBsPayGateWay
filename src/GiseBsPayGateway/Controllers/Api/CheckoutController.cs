using GiseBsPayGateway.Authentication;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public CheckoutController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("session")]
    public async Task<ActionResult<CheckoutSessionResponse>> CreateSession(
        [FromBody] CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var result = await _paymentService.CreateCheckoutSessionAsync(app, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }
}
