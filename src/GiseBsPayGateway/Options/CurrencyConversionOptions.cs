namespace GiseBsPayGateway.Options;

/// <summary>
/// Conversion de devises pour variantes de plans auto-créées.
/// Taux live via Banque du Canada ; RatesToCad sert de repli.
/// </summary>
public class CurrencyConversionOptions
{
    public const string SectionName = "CurrencyConversion";

    /// <summary>Si false, un conflit de devise lève une erreur au lieu de créer une variante.</summary>
    public bool AutoCreatePlanVariant { get; set; } = true;

    /// <summary>Utiliser les taux quotidiens Banque du Canada (sinon RatesToCad uniquement).</summary>
    public bool UseLiveRates { get; set; } = true;

    /// <summary>Durée de cache des taux live (minutes).</summary>
    public int LiveRatesCacheMinutes { get; set; } = 60;

    /// <summary>URL Valet BoC (séries FX*CAD).</summary>
    public string BankOfCanadaUrl { get; set; } =
        "https://www.bankofcanada.ca/valet/observations/FXUSDCAD,FXEURCAD,FXGBPCAD,FXCHFCAD/json?recent=1";

    /// <summary>
    /// Repli si l'API BoC est indisponible (1 unité = X CAD).
    /// </summary>
    public Dictionary<string, decimal> RatesToCad { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cad"] = 1m,
        ["usd"] = 1.36m,
        ["eur"] = 1.48m,
        ["gbp"] = 1.72m,
        ["chf"] = 1.55m
    };
}
