using System.ComponentModel.DataAnnotations;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Services.Tax;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiseBsPayGateway.Pages.Admin.TaxRates;

public class IndexModel : PageModel
{
    private readonly IAfricanTaxService _africanTax;
    private readonly IAuditService _audit;

    public IndexModel(IAfricanTaxService africanTax, IAuditService audit)
    {
        _africanTax = africanTax;
        _audit = audit;
    }

    public IReadOnlyList<AfricanTaxRateDto> Rates { get; private set; } = [];

    [BindProperty]
    public EditInput Input { get; set; } = new();

    public class EditInput
    {
        [Required, StringLength(2, MinimumLength = 2)]
        public string CountryCode { get; set; } = string.Empty;

        [Range(0, 100)]
        [Display(Name = "Taux (%)")]
        public decimal RatePercent { get; set; }

        [StringLength(500)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await _africanTax.EnsureSeededAsync(cancellationToken);
        Rates = _africanTax.ListRates();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _africanTax.UpdateRateAsync(Input.CountryCode, Input.RatePercent, Input.Notes, cancellationToken);
            await _audit.LogAsync(
                "AfricanTaxRateUpdated",
                "AfricanTaxRateSetting",
                Input.CountryCode.ToUpperInvariant(),
                true,
                $"Rate={Input.RatePercent}; Notes={Input.Notes}",
                userName: User.Identity?.Name);
            TempData["TaxRateMessage"] = Input.RatePercent == 0
                ? $"Pays {Input.CountryCode.ToUpperInvariant()} : exonéré (0 %)."
                : $"Taux {Input.CountryCode.ToUpperInvariant()} mis à jour : {Input.RatePercent:0.####} %.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TaxRateError"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExemptAsync(string countryCode, CancellationToken cancellationToken)
    {
        try
        {
            await _africanTax.UpdateRateAsync(countryCode, 0m, null, cancellationToken);
            await _audit.LogAsync(
                "AfricanTaxRateExempted",
                "AfricanTaxRateSetting",
                countryCode.ToUpperInvariant(),
                true,
                "Rate=0 (exonéré)",
                userName: User.Identity?.Name);
            TempData["TaxRateMessage"] = $"Pays {countryCode.ToUpperInvariant()} exonéré (0 %).";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TaxRateError"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(string countryCode, CancellationToken cancellationToken)
    {
        try
        {
            await _africanTax.RestoreStandardRateAsync(countryCode, cancellationToken);
            await _audit.LogAsync(
                "AfricanTaxRateRestored",
                "AfricanTaxRateSetting",
                countryCode.ToUpperInvariant(),
                true,
                "Restauration du taux standard publié",
                userName: User.Identity?.Name);
            TempData["TaxRateMessage"] = $"Taux standard restauré pour {countryCode.ToUpperInvariant()}.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TaxRateError"] = ex.Message;
        }

        return RedirectToPage();
    }
}
