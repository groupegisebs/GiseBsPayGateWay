using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("{paymentCode}")]
    public async Task<ActionResult<PaymentResponse>> GetPayment(string paymentCode, CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var payment = await _paymentService.GetPaymentByCodeAsync(app, paymentCode, cancellationToken);
        return payment is null ? NotFound(new ApiErrorResponse("Paiement introuvable.", null)) : Ok(payment);
    }
}
