using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public CustomersController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("{customerCode}/subscriptions")]
    public async Task<ActionResult<IReadOnlyList<SubscriptionResponse>>> GetSubscriptions(string customerCode, CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var subscriptions = await _paymentService.GetCustomerSubscriptionsAsync(app, customerCode, cancellationToken);
        return Ok(subscriptions);
    }
}
