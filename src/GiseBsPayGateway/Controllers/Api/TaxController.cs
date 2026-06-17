using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/tax")]
public class TaxController : ControllerBase
{
    private readonly ITaxService _taxService;

    public TaxController(ITaxService taxService)
    {
        _taxService = taxService;
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
}
