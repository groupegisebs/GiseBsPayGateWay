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
    /// Initie un paiement mobile money via Flutterwave (Orange Money, MTN MoMo, Wave, M-Pesa…).
    /// Le client valide sur son téléphone ; le statut final arrive par webhook ou GET /api/payments/{code}.
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
