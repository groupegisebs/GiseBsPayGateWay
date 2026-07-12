using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Configuration;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GiseBsPayGateway.Pages.Admin.StripeSettings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;
    private readonly IStripeSettingsProvider _stripeSettings;

    public IndexModel(ApplicationDbContext db, IAuditService auditService, IStripeSettingsProvider stripeSettings)
    {
        _db = db;
        _auditService = auditService;
        _stripeSettings = stripeSettings;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsConfiguredFromServerFile { get; private set; }
    public bool HasTestSecrets { get; private set; }
    public string? ServerSecretsFilePath { get; private set; }
    public string MaskedPublishableKey { get; private set; } = string.Empty;
    public string MaskedLivePublishableKey { get; private set; } = string.Empty;
    public string MaskedTestPublishableKey { get; private set; } = string.Empty;
    public bool IsLiveMode { get; private set; }

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
        IsConfiguredFromServerFile = _stripeSettings.IsConfiguredFromServerFile;
        HasTestSecrets = _stripeSettings.HasTestSecrets;
        ServerSecretsFilePath = ServerSecretsConfiguration.ResolveSecretsFilePath(HttpContext.RequestServices.GetRequiredService<IConfiguration>());

        var all = await _stripeSettings.GetAllConfiguredAsync(cancellationToken);
        foreach (var item in all)
        {
            if (item.IsLiveMode)
                MaskedLivePublishableKey = MaskKey(item.PublishableKey);
            else
                MaskedTestPublishableKey = MaskKey(item.PublishableKey);
        }

        var active = await _stripeSettings.GetActiveAsync(cancellationToken);
        if (active is not null)
        {
            MaskedPublishableKey = MaskKey(active.PublishableKey);
            IsLiveMode = active.IsLiveMode;
            Input.PublishableKey = active.PublishableKey;
            Input.IsLiveMode = active.IsLiveMode;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (_stripeSettings.IsConfiguredFromServerFile)
        {
            TempData["StripeError"] = "Les clés Stripe sont définies dans secrets.json sur le serveur. Modifiez ce fichier puis redémarrez le service.";
            return RedirectToPage();
        }

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
        await _auditService.LogAsync("StripeSettingsUpdated", nameof(Entities.StripeSettings), settings.Id.ToString(), true,
            $"LiveMode={settings.IsLiveMode}", userName: User.Identity?.Name);

        return RedirectToPage();
    }

    private static string MaskKey(string key) =>
        key.Length <= 8 ? "****" : $"{key[..4]}...{key[^4..]}";
}
