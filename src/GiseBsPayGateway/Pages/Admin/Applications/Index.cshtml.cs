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
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        IApiKeyService apiKeyService,
        IAuditService auditService,
        ILogger<IndexModel> logger)
    {
        _db = db;
        _apiKeyService = apiKeyService;
        _auditService = auditService;
        _logger = logger;
    }

    public IList<ApplicationViewModel> Applications { get; private set; } = [];
    public string? NewApiKey { get; private set; }
    public string? LoadError { get; private set; }

    public record ApplicationViewModel(Guid Id, string AppCode, string Name, string? AllowedDomains, bool IsActive, int ApiKeyCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var apps = await _db.ClientApplications
                .AsNoTracking()
                .Include(x => x.ApiKeys)
                .OrderBy(x => x.AppCode)
                .ToListAsync(cancellationToken);

            Applications = apps
                .Select(x => new ApplicationViewModel(
                    x.Id,
                    x.AppCode,
                    x.Name,
                    x.AllowedDomains,
                    x.IsActive,
                    x.ApiKeys.Count))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de charger les applications clientes");
            LoadError = "Impossible de charger la liste. Vérifiez la connexion PostgreSQL et les migrations EF.";
        }
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
        await _auditService.LogAsync("ApiKeyGenerated", nameof(ApplicationApiKey), applicationId.ToString(), true, $"Prefix={prefix}", app.AppCode, userName: User.Identity?.Name);

        NewApiKey = rawKey;
        await OnGetAsync(cancellationToken);
        return Page();
    }
}
