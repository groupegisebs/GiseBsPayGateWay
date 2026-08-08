namespace GiseBsPayGateway.Options;

/// <summary>
/// Conversion de devises pour variantes de plans auto-créées.
/// Taux live via Banque du Canada (groupe FX_RATES_DAILY) ; RatesToCad sert de repli.
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

    /// <summary>URL Valet BoC — groupe de tous les taux quotidiens FX→CAD.</summary>
    public string BankOfCanadaUrl { get; set; } =
        "https://www.bankofcanada.ca/valet/observations/group/FX_RATES_DAILY/json?recent=1";

    /// <summary>
    /// Repli si l'API BoC est indisponible (1 unité = X CAD).
    /// </summary>
    public Dictionary<string, decimal> RatesToCad { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cad"] = 1m,
        ["usd"] = 1.36m,
        ["eur"] = 1.48m,
        ["gbp"] = 1.72m,
        ["chf"] = 1.55m,
        ["jpy"] = 0.0092m,
        ["aud"] = 0.90m,
        ["nzd"] = 0.82m,
        ["cny"] = 0.19m,
        ["hkd"] = 0.17m,
        ["sgd"] = 1.02m,
        ["inr"] = 0.016m,
        ["krw"] = 0.0010m,
        ["sek"] = 0.13m,
        ["nok"] = 0.13m,
        ["mxn"] = 0.075m,
        ["brl"] = 0.25m,
        ["zar"] = 0.075m,
        ["pln"] = 0.34m,
        ["thb"] = 0.039m,
        ["myr"] = 0.31m,
        ["twd"] = 0.042m,
        ["try"] = 0.040m,
        ["idr"] = 0.000085m,
        ["pen"] = 0.37m
    };
}
