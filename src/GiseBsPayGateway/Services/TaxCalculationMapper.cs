using GiseBsPayGateway.DTOs;
using Stripe.Tax;

namespace GiseBsPayGateway.Services;

public static class TaxCalculationMapper
{
    private static readonly HashSet<string> VatCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GB", "GR",
        "HR", "HU", "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK"
    };

    public static TaxCalculationResponse? Map(Calculation calculation, BillingAddressDto address)
    {
        if (calculation.TaxBreakdown is null || calculation.TaxBreakdown.Count == 0)
        {
            return null;
        }

        var components = BuildComponents(calculation.TaxBreakdown);
        if (components.Count == 0)
        {
            return null;
        }

        var jurisdictionCode = BuildJurisdictionCode(address.Country, address.State);
        var taxLabels = components.Select(c => c.Name.ToLowerInvariant()).Distinct().ToList();
        var estimatedTaxRate = components.Sum(c => c.Rate);

        return new TaxCalculationResponse(
            jurisdictionCode,
            estimatedTaxRate,
            taxLabels,
            components);
    }

    public static string BuildJurisdictionCode(string country, string? state)
    {
        var normalizedCountry = country.Trim().ToUpperInvariant();
        var normalizedState = state?.Trim().ToUpperInvariant();

        return normalizedCountry switch
        {
            "CA" when !string.IsNullOrEmpty(normalizedState) => $"CA-{normalizedState}",
            "US" when !string.IsNullOrEmpty(normalizedState) => $"US-{normalizedState}",
            _ when VatCountries.Contains(normalizedCountry) => $"VAT-{normalizedCountry}",
            _ => normalizedCountry
        };
    }

    private static List<TaxComponentDto> BuildComponents(IEnumerable<CalculationTaxBreakdown> breakdown)
    {
        var components = new List<TaxComponentDto>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in breakdown)
        {
            var details = item.TaxRateDetails;
            if (details is null)
            {
                continue;
            }

            var rate = ParsePercentageRate(details.PercentageDecimal);
            if (rate <= 0)
            {
                continue;
            }

            var taxType = NormalizeTaxType(details.TaxType);
            var country = (details.Country ?? "XX").Trim().ToUpperInvariant();
            var code = $"{country}_{taxType}".ToLowerInvariant();
            if (!seenCodes.Add(code))
            {
                continue;
            }

            var displayName = taxType.ToUpperInvariant();
            components.Add(new TaxComponentDto(
                code,
                displayName,
                rate,
                ResolveComponentType(country, taxType, details.State)));
        }

        return components;
    }

    public static decimal ParsePercentageRate(string? percentageDecimal)
    {
        if (string.IsNullOrWhiteSpace(percentageDecimal))
        {
            return 0;
        }

        if (!decimal.TryParse(percentageDecimal, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return 0;
        }

        return value / 100m;
    }

    private static string NormalizeTaxType(string? taxType)
    {
        if (string.IsNullOrWhiteSpace(taxType))
        {
            return "tax";
        }

        return taxType.Trim().Replace(' ', '_').ToLowerInvariant();
    }

    private static string ResolveComponentType(string country, string taxType, string? state)
    {
        if (country is "CA")
        {
            return taxType switch
            {
                "gst" => "federal",
                "hst" => "combined",
                "qst" or "pst" or "rst" => "provincial",
                _ => string.IsNullOrWhiteSpace(state) ? "federal" : "provincial"
            };
        }

        if (country is "US")
        {
            return "state";
        }

        if (taxType is "vat")
        {
            return "vat";
        }

        return "standard";
    }
}
