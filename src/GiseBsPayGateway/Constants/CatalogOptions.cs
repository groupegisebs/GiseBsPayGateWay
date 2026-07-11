using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Constants;

public static class CatalogOptions
{
    public const string Monthly = "MONTHLY";
    public const string Yearly = "YEARLY";
    public const string OneTime = "ONE-TIME";

    public static readonly IReadOnlyList<PlanCodeOption> PlanCodes =
    [
        new(Monthly, "Mensuel", BillingInterval.Monthly),
        new(Yearly, "Annuel", BillingInterval.Yearly),
        new(OneTime, "Paiement unique", BillingInterval.OneTime)
    ];

    public static readonly IReadOnlyList<CurrencyOption> Currencies =
    [
        new("usd", "USD — Dollar US"),
        new("eur", "EUR — Euro"),
        new("gbp", "GBP — Livre sterling"),
        new("cad", "CAD — Dollar canadien"),
        new("chf", "CHF — Franc suisse"),
        new("jpy", "JPY — Yen japonais"),
        new("aud", "AUD — Dollar australien"),
        new("nzd", "NZD — Dollar néo-zélandais"),
        new("cny", "CNY — Yuan chinois"),
        new("hkd", "HKD — Dollar de Hong Kong"),
        new("sgd", "SGD — Dollar de Singapour"),
        new("inr", "INR — Roupie indienne"),
        new("krw", "KRW — Won sud-coréen"),
        new("sek", "SEK — Couronne suédoise"),
        new("nok", "NOK — Couronne norvégienne"),
        new("mxn", "MXN — Peso mexicain"),
        new("brl", "BRL — Real brésilien"),
        new("zar", "ZAR — Rand sud-africain"),
        new("pln", "PLN — Zloty polonais"),
        new("thb", "THB — Baht thaïlandais"),
        new("myr", "MYR — Ringgit malaisien"),
        new("twd", "TWD — Dollar taïwanais"),
        new("try", "TRY — Livre turque"),
        new("idr", "IDR — Roupie indonésienne"),
        new("pen", "PEN — Sol péruvien")
    ];

    public record PlanCodeOption(string Code, string Label, BillingInterval BillingInterval);

    public record CurrencyOption(string Code, string Label);

    public record CatalogOptionsResponse(
        IReadOnlyList<PlanCodeOption> PlanCodes,
        IReadOnlyList<CurrencyOption> Currencies);

    public static bool TryGetPlanCode(string? value, out PlanCodeOption option)
    {
        option = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        option = PlanCodes.FirstOrDefault(x =>
            x.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))!;

        return option is not null;
    }

    public static bool TryGetCurrency(string? value, out string normalizedCurrency)
    {
        normalizedCurrency = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        normalizedCurrency = value.Trim().ToLowerInvariant();
        foreach (var currency in Currencies)
        {
            if (currency.Code == normalizedCurrency)
            {
                return true;
            }
        }

        normalizedCurrency = string.Empty;
        return false;
    }

    public static PlanCodeOption ResolvePlanCode(string planCode)
    {
        if (!TryGetPlanCode(planCode, out var option))
        {
            throw new InvalidOperationException(
                $"Code plan invalide '{planCode}'. Valeurs acceptées : {string.Join(", ", PlanCodes.Select(x => x.Code))}.");
        }

        return option;
    }

    public static string ResolveCurrency(string currency)
    {
        if (!TryGetCurrency(currency, out var normalized))
        {
            throw new InvalidOperationException(
                $"Devise invalide '{currency}'. Valeurs acceptées : {string.Join(", ", Currencies.Select(x => x.Code.ToUpperInvariant()))}.");
        }

        return normalized;
    }
}
