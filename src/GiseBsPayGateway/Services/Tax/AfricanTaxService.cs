using GiseBsPayGateway.Constants;

namespace GiseBsPayGateway.Services.Tax;

public interface IAfricanTaxService
{
    /// <summary>
    /// Calcule HT / taxe / TTC pour un pays africain.
    /// <paramref name="netAmountExclusive"/> = montant catalogue hors taxe.
    /// </summary>
    AfricanTaxBreakdown Calculate(decimal netAmountExclusive, string currency, string countryCode);

    IReadOnlyList<AfricanTaxRateDto> ListRates();
}

public sealed record AfricanTaxBreakdown(
    string CountryCode,
    string CountryName,
    string TaxName,
    decimal TaxRatePercent,
    decimal AmountExclusive,
    decimal TaxAmount,
    decimal AmountInclusive,
    string Currency);

public sealed record AfricanTaxRateDto(
    string CountryCode,
    string CountryName,
    string TaxName,
    decimal RatePercent,
    string? Notes);

public sealed class AfricanTaxService : IAfricanTaxService
{
    public AfricanTaxBreakdown Calculate(decimal netAmountExclusive, string currency, string countryCode)
    {
        if (netAmountExclusive < 0)
            throw new InvalidOperationException("Le montant HT ne peut pas être négatif.");

        var code = (countryCode ?? "").Trim().ToUpperInvariant();
        if (!AfricanTaxRates.TryGet(code, out var rate))
            throw new InvalidOperationException(
                $"Pays '{countryCode}' non supporté pour le calcul de taxe Afrique. " +
                "Fournissez un code ISO 3166-1 alpha-2 africain (ex. CM, SN, CI, NG).");

        var currencyNorm = (currency ?? "XAF").Trim().ToUpperInvariant();
        var isZeroDecimal = CatalogOptions.IsZeroDecimalCurrency(currencyNorm);

        var exclusive = RoundMoney(netAmountExclusive, isZeroDecimal);
        var tax = RoundMoney(exclusive * rate.RatePercent / 100m, isZeroDecimal);
        var inclusive = RoundMoney(exclusive + tax, isZeroDecimal);

        return new AfricanTaxBreakdown(
            rate.CountryCode,
            rate.CountryNameFr,
            rate.TaxName,
            rate.RatePercent,
            exclusive,
            tax,
            inclusive,
            currencyNorm);
    }

    public IReadOnlyList<AfricanTaxRateDto> ListRates() =>
        AfricanTaxRates.AllOrdered()
            .Select(r => new AfricanTaxRateDto(r.CountryCode, r.CountryNameFr, r.TaxName, r.RatePercent, r.Notes))
            .ToList();

    private static decimal RoundMoney(decimal value, bool zeroDecimal) =>
        zeroDecimal
            ? Math.Round(value, 0, MidpointRounding.AwayFromZero)
            : Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
