using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Pages.Admin.StripeSettings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;

    public IndexModel(ApplicationDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Entities.StripeSettings? CurrentSettings { get; private set; }
    public string MaskedPublishableKey { get; private set; } = string.Empty;

    public class InputModel
    {
        [Required, Display(Name = "Clé publique")]
        public string PublishableKey { get; set; } = string.Empty;

        [Required, Display(Name = "Clé secrète")]
        public string SecretKey { get; set; } = string.Empty;

        [Required, Display(Name = "Secret webhook")]
        public string WebhookSecret { get; set; } = string.Empty;

        [Display(Name = "Mode production (Live)")]
        public bool IsLiveMode { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CurrentSettings = await _db.StripeSettings
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (CurrentSettings is not null)
        {
            Input.PublishableKey = CurrentSettings.PublishableKey;
            Input.IsLiveMode = CurrentSettings.IsLiveMode;
            MaskedPublishableKey = MaskKey(CurrentSettings.PublishableKey);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var existing = await _db.StripeSettings.Where(x => x.IsActive).ToListAsync(cancellationToken);
        foreach (var item in existing)
        {
            item.IsActive = false;
            item.UpdatedAt = DateTime.UtcNow;
        }

        var settings = new Entities.StripeSettings
        {
            PublishableKey = Input.PublishableKey.Trim(),
            SecretKey = Input.SecretKey.Trim(),
            WebhookSecret = Input.WebhookSecret.Trim(),
            IsLiveMode = Input.IsLiveMode,
            IsActive = true
        };

        _db.StripeSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("StripeSettingsUpdated", nameof(StripeSettings), settings.Id.ToString(), true,
            $"LiveMode={settings.IsLiveMode}", userName: User.Identity?.Name);

        return RedirectToPage();
    }

    private static string MaskKey(string key) =>
        key.Length <= 8 ? "****" : $"{key[..4]}...{key[^4..]}";
}
