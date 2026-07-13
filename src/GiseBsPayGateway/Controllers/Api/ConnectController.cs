using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/connect")]
public class ConnectController(IConnectService connectService) : ControllerBase
{
    [HttpPost("accounts")]
    public async Task<ActionResult<ConnectAccountResponse>> CreateAccount(
        [FromBody] CreateConnectAccountRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            return Ok(await connectService.CreateAccountAsync(app, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpPost("account-links")]
    public async Task<ActionResult<ConnectAccountLinkResponse>> CreateAccountLink(
        [FromBody] CreateConnectAccountLinkRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            return Ok(await connectService.CreateAccountLinkAsync(app, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpGet("accounts/{externalAccountId}")]
    public async Task<ActionResult<ConnectAccountResponse>> GetAccount(
        string externalAccountId,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            return Ok(await connectService.GetAccountAsync(app, externalAccountId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }
}
