using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/tax")]
public class TaxController : ControllerBase
{
    private readonly ITaxService _taxService;
    private readonly ICollectedTaxService _collectedTaxService;

    public TaxController(ITaxService taxService, ICollectedTaxService collectedTaxService)
    {
        _taxService = taxService;
        _collectedTaxService = collectedTaxService;
    }

    [HttpPost("calculate")]
    public async Task<ActionResult<TaxCalculationResponse>> Calculate(
        [FromBody] TaxCalculationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _taxService.CalculateAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (TaxCalculationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("collected")]
    public async Task<ActionResult<IReadOnlyList<CollectedTaxSummaryDto>>> ListCollected(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var records = await _collectedTaxService.ListCollectedAsync(app.Id, from, to, cancellationToken);
        return Ok(records);
    }
}
