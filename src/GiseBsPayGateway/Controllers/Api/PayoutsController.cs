using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/payouts")]
public class PayoutsController(
    ITransferService transferService,
    IDisbursementQueueService disbursements,
    IMobileMoneyPublicInfoService mobileMoney) : ControllerBase
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

    /// <summary>File d'attente : revue + rapprochement admin avant paiement.</summary>
    [HttpPost("disbursement-requests")]
    public async Task<ActionResult<DisbursementRequestResponse>> EnqueueDisbursement(
        [FromBody] CreateDisbursementRequestDto request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            return Ok(await disbursements.EnqueueAsync(app, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("disbursement-requests/{reference}")]
    public async Task<ActionResult<DisbursementRequestResponse>> GetDisbursement(
        string reference,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var result = await disbursements.GetAsync(app, reference, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("mobile-money/validate")]
    public ActionResult<MobileMoneyValidateResponse> ValidateMobileMoney([FromBody] MobileMoneyValidateRequest request)
        => Ok(mobileMoney.ValidatePublicInfo(request));

    [HttpPost("mobile-money/recipients")]
    public async Task<ActionResult<object>> RegisterMobileMoney(
        [FromBody] RegisterMobileMoneyRecipientRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var recipient = await mobileMoney.RegisterAsync(app, request, cancellationToken);
            return Ok(new
            {
                recipient.ExternalReference,
                recipient.OperatorCode,
                recipient.CountryCode,
                recipient.MaskedPhone,
                recipient.AccountHolderName,
                recipient.PublicAccountId,
                recipient.Status
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }
}
