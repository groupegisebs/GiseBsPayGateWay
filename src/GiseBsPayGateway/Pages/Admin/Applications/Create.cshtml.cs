using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiseBsPayGateway.Pages.Admin.Applications;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAuditService _auditService;

    public CreateModel(ApplicationDbContext db, IApiKeyService apiKeyService, IAuditService auditService)
    {
        _db = db;
        _apiKeyService = apiKeyService;
        _auditService = auditService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, MaxLength(50), Display(Name = "AppCode")]
        public string AppCode { get; set; } = string.Empty;

        [Required, MaxLength(200), Display(Name = "Nom")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Display(Name = "Domaines autorisés (séparés par des virgules)")]
        public string? AllowedDomains { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var app = new ClientApplication
        {
            AppCode = Input.AppCode.ToUpperInvariant(),
            Name = Input.Name,
            Description = Input.Description,
            AllowedDomains = Input.AllowedDomains,
            IsActive = true
        };

        _db.ClientApplications.Add(app);
        await _db.SaveChangesAsync(cancellationToken);

        var (rawKey, prefix, hash) = _apiKeyService.GenerateApiKey();
        _db.ApplicationApiKeys.Add(new ApplicationApiKey
        {
            ClientApplicationId = app.Id,
            Name = "Clé initiale",
            KeyPrefix = prefix,
            KeyHash = hash,
            IsActive = true
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync("ClientApplicationCreated", nameof(ClientApplication), app.Id.ToString(), true, app.AppCode, userName: User.Identity?.Name);
        TempData["NewApiKey"] = rawKey;
        return RedirectToPage("Index");
    }
}
