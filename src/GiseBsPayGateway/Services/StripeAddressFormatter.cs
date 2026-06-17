using System.Text.RegularExpressions;
using GiseBsPayGateway.DTOs;

namespace GiseBsPayGateway.Services;

/// <summary>
/// Defensive normalization before Stripe Customer.address updates (Stripe Tax).
/// BoutiqueGise formats upstream; this ensures Pay Gateway sends Stripe-compliant values.
/// </summary>
public static class StripeAddressFormatter
{
    private static readonly HashSet<string> CanadianProvinceCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT"
    };

    private static readonly HashSet<string> UsStateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN", "IA",
        "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM",
        "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA",
        "WV", "WI", "WY", "AS", "GU", "MP", "PR", "VI", "AA", "AE", "AP"
    };

    private static readonly Regex CanadianPostalRegex = new(
        @"^[A-Z]\d[A-Z]\s?\d[A-Z]\d$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UsPostalRegex = new(
        @"^\d{5}(-\d{4})?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static BillingAddressDto Format(BillingAddressDto address)
    {
        var country = address.Country.Trim().ToUpperInvariant();
        var line1 = address.Line1.Trim();
        var line2 = string.IsNullOrWhiteSpace(address.Line2) ? null : address.Line2.Trim();
        var city = address.City.Trim();
        var state = NormalizeState(country, address.State);
        var postalCode = NormalizePostalCode(country, address.PostalCode) ?? address.PostalCode.Trim();

        return new BillingAddressDto(line1, line2, city, state, postalCode, country);
    }

    private static string? NormalizeState(string country, string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        var code = state.Trim().ToUpperInvariant();
        return country switch
        {
            "CA" when CanadianProvinceCodes.Contains(code) => code,
            "US" when UsStateCodes.Contains(code) => code,
            _ => state.Trim()
        };
    }

    private static string? NormalizePostalCode(string country, string postalCode)
    {
        var trimmed = postalCode.Trim();
        return country switch
        {
            "CA" => NormalizeCanadianPostalCode(trimmed),
            "US" => NormalizeUsPostalCode(trimmed),
            _ => trimmed
        };
    }

    private static string? NormalizeCanadianPostalCode(string postalCode)
    {
        var compact = new string(postalCode.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length != 6)
            return null;

        var formatted = $"{compact[..3]} {compact[3..]}";
        return CanadianPostalRegex.IsMatch(formatted) ? formatted : null;
    }

    private static string? NormalizeUsPostalCode(string postalCode)
    {
        var digits = new string(postalCode.Where(char.IsDigit).ToArray());
        if (digits.Length is 5)
            return UsPostalRegex.IsMatch(digits) ? digits : null;

        if (digits.Length is 9)
        {
            var formatted = $"{digits[..5]}-{digits[5..]}";
            return UsPostalRegex.IsMatch(formatted) ? formatted : null;
        }

        var trimmed = postalCode.Trim();
        return UsPostalRegex.IsMatch(trimmed) ? trimmed : null;
    }
}
