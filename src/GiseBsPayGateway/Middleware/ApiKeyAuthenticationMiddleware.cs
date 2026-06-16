using GiseBsPayGateway.Authentication;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Middleware;

public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyOptions _options;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, IApiKeyService apiKeyService, IAuditService auditService)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/api/webhooks/stripe") ||
            context.Request.Path.StartsWithSegments("/api/auth/token"))
        {
            await _next(context);
            return;
        }

        var jwtResult = await TryAuthenticateJwtAsync(context, db, auditService);
        if (jwtResult == JwtAuthenticationResult.Succeeded)
        {
            await _next(context);
            return;
        }

        if (jwtResult == JwtAuthenticationResult.Failed)
            return;

        if (!context.Request.Headers.TryGetValue(_options.AppCodeHeaderName, out var appCode) ||
            !context.Request.Headers.TryGetValue(_options.HeaderName, out var apiKey))
        {
            await auditService.LogAsync("ApiKeyAuthFailed", "ApiKey", null, false, "Headers manquants");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "AppCode et API Key requis." });
            return;
        }

        var application = await db.ClientApplications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppCode == appCode.ToString() && x.IsActive);

        if (application is null)
        {
            await auditService.LogAsync("ApiKeyAuthFailed", "ClientApplication", null, false, "AppCode invalide", appCode);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Application cliente invalide." });
            return;
        }

        if (!string.IsNullOrWhiteSpace(application.AllowedDomains))
        {
            var origin = context.Request.Headers.Origin.ToString();
            var referer = context.Request.Headers.Referer.ToString();
            var allowed = application.AllowedDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var domainValid = allowed.Any(d =>
                (!string.IsNullOrEmpty(origin) && origin.Contains(d, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(referer) && referer.Contains(d, StringComparison.OrdinalIgnoreCase)));

            if (!domainValid && !string.IsNullOrEmpty(origin))
            {
                await auditService.LogAsync("DomainValidationFailed", "ClientApplication", application.Id.ToString(), false, origin, application.AppCode);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Domaine non autorisé." });
                return;
            }
        }

        var keys = await db.ApplicationApiKeys
            .Where(x => x.ClientApplicationId == application.Id && x.IsActive)
            .ToListAsync();

        var matchedKey = keys.FirstOrDefault(k =>
            apiKeyService.VerifyApiKey(apiKey.ToString(), k.KeyHash) &&
            (k.ExpiresAt is null || k.ExpiresAt > DateTime.UtcNow));

        if (matchedKey is null)
        {
            await auditService.LogAsync("ApiKeyAuthFailed", "ApplicationApiKey", null, false, "Clé invalide", application.AppCode);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API Key invalide." });
            return;
        }

        matchedKey.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        context.Items[nameof(ClientApplicationContext)] = new ClientApplicationContext
        {
            Application = application,
            ApiKey = matchedKey
        };

        await _next(context);
    }

    private enum JwtAuthenticationResult
    {
        NotAttempted,
        Succeeded,
        Failed
    }

    private static async Task<JwtAuthenticationResult> TryAuthenticateJwtAsync(
        HttpContext context,
        ApplicationDbContext db,
        IAuditService auditService)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtAuthenticationResult.NotAttempted;
        }

        var authResult = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            await auditService.LogAsync("JwtAuthFailed", "ClientApplication", null, false, "Bearer token invalide");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Jeton Bearer invalide ou expiré." });
            return JwtAuthenticationResult.Failed;
        }

        var appCode = authResult.Principal.FindFirst("app_code")?.Value;
        if (string.IsNullOrWhiteSpace(appCode))
        {
            await auditService.LogAsync("JwtAuthFailed", "ClientApplication", null, false, "Claim app_code manquant");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Jeton Bearer invalide." });
            return JwtAuthenticationResult.Failed;
        }

        var application = await db.ClientApplications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppCode == appCode && x.IsActive);

        if (application is null)
        {
            await auditService.LogAsync("JwtAuthFailed", "ClientApplication", null, false, "AppCode invalide", appCode);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Application cliente invalide." });
            return JwtAuthenticationResult.Failed;
        }

        if (!string.IsNullOrWhiteSpace(application.AllowedDomains) &&
            !IsAllowedDomain(context, application.AllowedDomains))
        {
            var origin = context.Request.Headers.Origin.ToString();
            await auditService.LogAsync("DomainValidationFailed", "ClientApplication", application.Id.ToString(), false, origin, application.AppCode);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Domaine non autorisé." });
            return JwtAuthenticationResult.Failed;
        }

        context.Items[nameof(ClientApplicationContext)] = new ClientApplicationContext
        {
            Application = application
        };

        return JwtAuthenticationResult.Succeeded;
    }

    private static bool IsAllowedDomain(HttpContext context, string allowedDomains)
    {
        var origin = context.Request.Headers.Origin.ToString();
        var referer = context.Request.Headers.Referer.ToString();
        if (string.IsNullOrEmpty(origin) && string.IsNullOrEmpty(referer))
            return true;

        var allowed = allowedDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(d =>
            (!string.IsNullOrEmpty(origin) && origin.Contains(d, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(referer) && referer.Contains(d, StringComparison.OrdinalIgnoreCase)));
    }
}

public static class ApiKeyAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
}
