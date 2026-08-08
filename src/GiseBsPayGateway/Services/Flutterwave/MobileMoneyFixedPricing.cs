using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Options;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services.Flutterwave;

/// <summary>
/// Grille tarifaire fixe Mobile Money : montants locaux équivalents à <b>10 USD</b>.
/// Les autres montants sont proportionnels (ex. 20 USD = 2× le tarif 10 USD).
/// </summary>
public interface IMobileMoneyFixedPricing
{
    /// <summary>Montant local pour exactement 10 USD (0 si devise non supportée).</summary>
    decimal GetAmountFor10Usd(string localCurrency);

    Task<decimal> ConvertToLocalAsync(
        decimal amount,
        string fromCurrency,
        string localCurrency,
        ICurrencyConversionService conversion,
        CancellationToken ct = default);
}

public sealed class MobileMoneyFixedPricing(IOptions<FlutterwaveOptions> options) : IMobileMoneyFixedPricing
{
    /// <summary>Valeurs commerciales arrondies ≈ 10 USD (août 2026).</summary>
    public static IReadOnlyDictionary<string, decimal> DefaultFor10Usd { get; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["XOF"] = 6_000m,   // BF, CI, SN
            ["XAF"] = 6_000m,   // CM
            ["GHS"] = 120m,     // GH
            ["KES"] = 1_300m,   // KE
            ["RWF"] = 15_000m,  // RW
            ["TZS"] = 27_000m,  // TZ
            ["UGX"] = 37_000m,  // UG
            ["ZMW"] = 180m      // ZM
        };

    public decimal GetAmountFor10Usd(string localCurrency)
    {
        var code = localCurrency.Trim().ToUpperInvariant();
        var configured = options.Value.FixedAmountsFor10Usd;
        if (configured is { Count: > 0 }
            && configured.TryGetValue(code, out var custom)
            && custom > 0)
        {
            return CatalogOptions.IsZeroDecimalCurrency(code)
                ? decimal.Round(custom, 0, MidpointRounding.AwayFromZero)
                : decimal.Round(custom, 2, MidpointRounding.AwayFromZero);
        }

        return DefaultFor10Usd.TryGetValue(code, out var amount) ? amount : 0m;
    }

    public async Task<decimal> ConvertToLocalAsync(
        decimal amount,
        string fromCurrency,
        string localCurrency,
        ICurrencyConversionService conversion,
        CancellationToken ct = default)
    {
        var local = localCurrency.Trim().ToUpperInvariant();
        var fixed10 = GetAmountFor10Usd(local);
        if (fixed10 <= 0)
        {
            // Repli : conversion live si devise hors grille.
            return await conversion.ConvertAsync(amount, fromCurrency, local, ct);
        }

        var from = fromCurrency.Trim().ToUpperInvariant();
        decimal usdAmount;
        if (from == "USD")
        {
            usdAmount = amount;
        }
        else
        {
            usdAmount = await conversion.ConvertAsync(amount, from, "USD", ct);
        }

        var localAmount = fixed10 * (usdAmount / 10m);
        return CurrencyConversionService.RoundForCurrency(localAmount, local);
    }
}
