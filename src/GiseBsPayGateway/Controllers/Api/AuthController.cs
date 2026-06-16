using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IApiKeyService _apiKeyService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditService _auditService;

    public AuthController(ApplicationDbContext db, IApiKeyService apiKeyService, IJwtTokenService jwtTokenService, IAuditService auditService)
    {
        _db = db;
        _apiKeyService = apiKeyService;
        _jwtTokenService = jwtTokenService;
        _auditService = auditService;
    }

    [HttpPost("token")]
    public async Task<ActionResult<JwtTokenResponse>> GetToken([FromBody] JwtTokenRequest request, CancellationToken cancellationToken)
    {
        var app = await _db.ClientApplications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppCode == request.AppCode && x.IsActive, cancellationToken);

        if (app is null)
        {
            return Unauthorized(new ApiErrorResponse("Application invalide.", null));
        }

        var keys = await _db.ApplicationApiKeys
            .Where(x => x.ClientApplicationId == app.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        var matched = keys.FirstOrDefault(k =>
            _apiKeyService.VerifyApiKey(request.ApiKey, k.KeyHash) &&
            (k.ExpiresAt is null || k.ExpiresAt > DateTime.UtcNow));

        if (matched is null)
        {
            await _auditService.LogAsync("JwtAuthFailed", "ApplicationApiKey", null, false, null, app.AppCode);
            return Unauthorized(new ApiErrorResponse("API Key invalide.", null));
        }

        var token = _jwtTokenService.GenerateToken(app);
        await _auditService.LogAsync("JwtTokenIssued", "ClientApplication", app.Id.ToString(), true, null, app.AppCode);
        return Ok(token);
    }
}
