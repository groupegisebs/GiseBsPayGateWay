using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Services.Tax;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/tax")]
public class TaxController : ControllerBase
{
    private readonly ITaxService _taxService;
    private readonly ICollectedTaxService _collectedTaxService;
    private readonly IAfricanTaxService _africanTaxService;

    public TaxController(
        ITaxService taxService,
        ICollectedTaxService collectedTaxService,
        IAfricanTaxService africanTaxService)
    {
        _taxService = taxService;
        _collectedTaxService = collectedTaxService;
        _africanTaxService = africanTaxService;
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

    /// <summary>Liste complète des taux TVA/GST des pays d'Afrique.</summary>
    [HttpGet("africa/rates")]
    public ActionResult<IReadOnlyList<AfricanTaxRateDto>> ListAfricaRates() =>
        Ok(_africanTaxService.ListRates());

    /// <summary>Calcule HT + taxe + TTC pour un pays africain (sans Stripe).</summary>
    [HttpPost("africa/quote")]
    public ActionResult<AfricanTaxQuoteResponse> QuoteAfrica([FromBody] AfricanTaxQuoteRequest request)
    {
        try
        {
            var result = _africanTaxService.Calculate(
                request.AmountExclusive,
                request.Currency,
                request.CountryCode);
            return Ok(new AfricanTaxQuoteResponse(
                result.CountryCode,
                result.CountryName,
                result.TaxName,
                result.TaxRatePercent,
                result.AmountExclusive,
                result.TaxAmount,
                result.AmountInclusive,
                result.Currency));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
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
