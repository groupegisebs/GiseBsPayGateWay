using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/payouts")]
public class PayoutsController(ITransferService transferService) : ControllerBase
{
    [HttpPost("transfers")]
    public async Task<ActionResult<ConnectTransferResponse>> CreateTransfer(
        [FromBody] CreateConnectTransferRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            return Ok(await transferService.CreateTransferAsync(app, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("transfers/{transferId}")]
    public async Task<ActionResult<ConnectTransferResponse>> GetTransfer(
        string transferId,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var result = await transferService.GetTransferAsync(app, transferId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Stub validation Mobile Money (Phase 4 — agrégateur).</summary>
    [HttpPost("mobile-money/validate")]
    public ActionResult<MobileMoneyValidateResponse> ValidateMobileMoney([FromBody] MobileMoneyValidateRequest request)
    {
        var phone = request.PhoneNumber?.Trim() ?? string.Empty;
        if (phone.Length < 8)
            return Ok(new MobileMoneyValidateResponse(false, null, null, "Numéro invalide."));

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var masked = digits.Length >= 4
            ? $"+{digits[..Math.Min(3, digits.Length)]} ••• ••• {digits[^2..]}"
            : "••••";

        var token = $"mm_{request.OperatorCode}_{Guid.NewGuid():N}"[..40];
        return Ok(new MobileMoneyValidateResponse(true, masked, token, null));
    }

    /// <summary>Stub décaissement Mobile Money.</summary>
    [HttpPost("mobile-money/disburse")]
    public ActionResult<object> DisburseMobileMoney([FromBody] CreateConnectTransferRequest request)
    {
        return Ok(new
        {
            transferId = $"mm_tx_{Guid.NewGuid():N}"[..24],
            request.IdempotencyKey,
            status = "queued",
            message = "Disbursement accepté (stub agrégateur)."
        });
    }
}
