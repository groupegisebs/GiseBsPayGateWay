namespace GiseBsPayGateway.Entities;

/// <summary>
/// Taux de taxe Afrique configurable depuis l'administration.
/// <see cref="RatePercent"/> = 0 → exonéré (TTC = HT).
/// </summary>
public class AfricanTaxRateSetting : BaseEntity
{
    public string CountryCode { get; set; } = string.Empty;
    public string CountryNameFr { get; set; } = string.Empty;
    public string TaxName { get; set; } = string.Empty;

    /// <summary>Taux appliqué (%) — 0 = exonéré.</summary>
    public decimal RatePercent { get; set; }

    /// <summary>Taux standard publié (référence pour restauration admin).</summary>
    public decimal StandardRatePercent { get; set; }

    public string? Notes { get; set; }
}
