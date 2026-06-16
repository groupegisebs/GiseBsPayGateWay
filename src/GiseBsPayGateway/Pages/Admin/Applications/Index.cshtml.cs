using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.Applications;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAuditService _auditService;

    public IndexModel(ApplicationDbContext db, IApiKeyService apiKeyService, IAuditService auditService)
    {
        _db = db;
        _apiKeyService = apiKeyService;
        _auditService = auditService;
    }

    public IList<ApplicationViewModel> Applications { get; private set; } = [];
    public string? NewApiKey { get; private set; }

    public record ApplicationViewModel(Guid Id, string AppCode, string Name, string? AllowedDomains, bool IsActive, int ApiKeyCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Applications = await _db.ClientApplications.AsNoTracking()
            .Select(x => new ApplicationViewModel(
                x.Id,
                x.AppCode,
                x.Name,
                x.AllowedDomains,
                x.IsActive,
                x.ApiKeys.Count))
            .OrderBy(x => x.AppCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostGenerateKeyAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var app = await _db.ClientApplications.FindAsync([applicationId], cancellationToken);
        if (app is null)
        {
            return NotFound();
        }

        var (rawKey, prefix, hash) = _apiKeyService.GenerateApiKey();
        _db.ApplicationApiKeys.Add(new ApplicationApiKey
        {
            ClientApplicationId = applicationId,
            Name = $"Clé {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            KeyPrefix = prefix,
            KeyHash = hash,
            IsActive = true
        });
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("ApiKeyGenerated", nameof(ApplicationApiKey), applicationId.ToString(), true, $"Prefix={prefix}", app.AppCode, User.Identity?.Name);

        NewApiKey = rawKey;
        await OnGetAsync(cancellationToken);
        return Page();
    }
}
