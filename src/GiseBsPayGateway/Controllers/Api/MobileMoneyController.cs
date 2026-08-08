using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services.Flutterwave;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/mobile-money")]
public class MobileMoneyController(IFlutterwaveMobileMoneyService mobileMoney) : ControllerBase
{
    /// <summary>Catalogue plat des opérateurs (filtre optionnel ?country=CM).</summary>
    [HttpGet("networks")]
    public ActionResult<IReadOnlyList<MobileMoneyNetworkDto>> Networks([FromQuery] string? country = null) =>
        Ok(mobileMoney.ListNetworks(country));

    /// <summary>
    /// Pays / devises Flutterwave Mobile Money :
    /// XOF (BF, CI, SN), XAF (CM), GHS, KES, RWF, TZS, UGX, ZMW.
    /// </summary>
    [HttpGet("countries")]
    public ActionResult<IReadOnlyList<MobileMoneyCountryDto>> Countries() => Ok(mobileMoney.ListCountries());

    /// <summary>
    /// Convertit un montant catalogue vers la devise Mobile Money du pays (ex. 10 USD → XAF pour CM).
    /// </summary>
    [HttpGet("quote")]
    public async Task<ActionResult<MobileMoneyQuoteResponse>> Quote(
        [FromQuery] decimal amount,
        [FromQuery] string currency,
        [FromQuery] string countryCode,
        CancellationToken ct)
    {
        try
        {
            return Ok(await mobileMoney.QuoteAsync(amount, currency, countryCode, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Initie un paiement mobile money via Flutterwave (Orange Money, MTN MoMo, Wave, M-Pesa…).
    /// Le montant est toujours converti dans la devise du pays sélectionné.
    /// </summary>
    [HttpPost("charge")]
    public async Task<ActionResult<MobileMoneyChargeResponse>> Charge(
        [FromBody] CreateMobileMoneyChargeRequest request,
        CancellationToken ct)
    {
        var app = HttpContext.GetClientApplicationContext().Application;

        try
        {
            var result = await mobileMoney.ChargeAsync(app, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
