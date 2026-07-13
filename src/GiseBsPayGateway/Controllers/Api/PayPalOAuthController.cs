using System.Text;
using System.Text.Json;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/paypal")]
public class PayPalOAuthController(
    IPayPalPayoutService paypal,
    Data.ApplicationDbContext db) : ControllerBase
{
    [HttpPost("oauth/start")]
    public ActionResult<PayPalOAuthStartResponse> StartOAuth([FromBody] PayPalOAuthStartRequest request)
    {
        if (!paypal.IsConfigured)
            return BadRequest(new ApiErrorResponse("PayPal non configuré sur PayGateway (section PayPal dans secrets).", null));

        var app = HttpContext.GetClientApplicationContext().Application;
        var stateObj = new
        {
            appId = app.Id,
            appCode = app.AppCode,
            externalReference = request.ExternalReference,
            returnUrl = request.ReturnUrl
        };
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stateObj)));
        try
        {
            var url = paypal.BuildAuthorizationUrl(state);
            return Ok(new PayPalOAuthStartResponse(url, state));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> OAuthCallback([FromQuery] string? code, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest("code/state manquants");

        Guid appId;
        string externalReference;
        string? returnUrl;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            using var doc = JsonDocument.Parse(raw);
            appId = doc.RootElement.GetProperty("appId").GetGuid();
            externalReference = doc.RootElement.GetProperty("externalReference").GetString() ?? "unknown";
            returnUrl = doc.RootElement.TryGetProperty("returnUrl", out var ru) ? ru.GetString() : null;
        }
        catch
        {
            return BadRequest("state invalide");
        }

        var app = await db.ClientApplications.FindAsync([appId], cancellationToken);
        if (app is null) return NotFound("Application introuvable");

        try
        {
            var linked = await paypal.CompleteOAuthAsync(app, code, externalReference, cancellationToken);
            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                var sep = returnUrl.Contains('?') ? "&" : "?";
                return Redirect($"{returnUrl}{sep}paypal=linked&ref={Uri.EscapeDataString(externalReference)}&email={Uri.EscapeDataString(linked.MaskedEmail ?? "")}");
            }

            return Ok(new PayPalLinkedAccountResponse(linked.ExternalReference, linked.MaskedEmail, linked.Status, linked.PayerId));
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("accounts/{externalReference}")]
    public async Task<ActionResult<PayPalLinkedAccountResponse>> GetAccount(string externalReference, CancellationToken ct)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var linked = await paypal.GetLinkedAsync(app, externalReference, ct);
        if (linked is null) return NotFound();
        return Ok(new PayPalLinkedAccountResponse(linked.ExternalReference, linked.MaskedEmail, linked.Status, linked.PayerId));
    }
}
