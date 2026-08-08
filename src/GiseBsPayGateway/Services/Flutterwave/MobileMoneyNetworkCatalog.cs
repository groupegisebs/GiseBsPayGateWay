using GiseBsPayGateway.DTOs;

namespace GiseBsPayGateway.Services.Flutterwave;

/// <summary>
/// Opérateurs Mobile Money supportés par Flutterwave (dashboard + API v4).
/// Source : availability XOF/XAF/GHS/KES/RWF/TZS/UGX/ZMW.
/// </summary>
public static class MobileMoneyNetworkCatalog
{
    public sealed record NetworkOption(
        string CountryCode,
        string CountryName,
        string Currency,
        string Network,
        string NetworkLabel,
        string PhoneCountryCode);

    public static IReadOnlyList<NetworkOption> All { get; } =
    [
        // ── XOF — Burkina Faso ───────────────────────────────────────────────
        new("BF", "Burkina Faso", "XOF", "ORANGE", "Orange Money", "226"),
        new("BF", "Burkina Faso", "XOF", "MOBICASH", "Mobicash", "226"),

        // ── XOF — Côte d'Ivoire ──────────────────────────────────────────────
        new("CI", "Côte d'Ivoire", "XOF", "MTN", "MTN", "225"),
        new("CI", "Côte d'Ivoire", "XOF", "ORANGE", "Orange", "225"),
        new("CI", "Côte d'Ivoire", "XOF", "WAVE", "Wave", "225"),

        // ── XOF — Senegal ────────────────────────────────────────────────────
        new("SN", "Senegal", "XOF", "ORANGE", "Orange", "221"),
        new("SN", "Senegal", "XOF", "FREEMONEY", "Free Money", "221"),
        new("SN", "Senegal", "XOF", "WAVE", "Wave", "221"),

        // ── XAF — Cameroon ───────────────────────────────────────────────────
        new("CM", "Cameroon", "XAF", "MTN", "MTN", "237"),
        new("CM", "Cameroon", "XAF", "ORANGE", "Orange", "237"),

        // ── GHS — Ghana ──────────────────────────────────────────────────────
        new("GH", "Ghana", "GHS", "MTN", "MTN", "233"),
        new("GH", "Ghana", "GHS", "TELECEL", "Telecel", "233"),
        new("GH", "Ghana", "GHS", "AIRTEL", "Airtel", "233"),

        // ── KES — Kenya ──────────────────────────────────────────────────────
        new("KE", "Kenya", "KES", "MPESA", "M-Pesa", "254"),

        // ── RWF — Rwanda ─────────────────────────────────────────────────────
        new("RW", "Rwanda", "RWF", "AIRTEL", "Airtel", "250"),
        new("RW", "Rwanda", "RWF", "MTN", "MTN", "250"),

        // ── TZS — Tanzania ───────────────────────────────────────────────────
        new("TZ", "Tanzania", "TZS", "AIRTEL", "Airtel", "255"),
        new("TZ", "Tanzania", "TZS", "TIGO", "Tigo", "255"),
        new("TZ", "Tanzania", "TZS", "HALOPESA", "Halopesa", "255"),

        // ── UGX — Uganda ─────────────────────────────────────────────────────
        new("UG", "Uganda", "UGX", "AIRTEL", "Airtel", "256"),
        new("UG", "Uganda", "UGX", "MTN", "MTN", "256"),

        // ── ZMW — Zambia ─────────────────────────────────────────────────────
        new("ZM", "Zambia", "ZMW", "AIRTEL", "Airtel", "260"),
        new("ZM", "Zambia", "ZMW", "MTN", "MTN", "260"),
        new("ZM", "Zambia", "ZMW", "ZAMTEL", "Zamtel", "260"),
    ];

    /// <summary>Alias acceptés (ex. ORANGEMONEY → ORANGE, MPS → MPESA).</summary>
    private static readonly Dictionary<string, string> NetworkAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORANGEMONEY"] = "ORANGE",
        ["ORANGE_MONEY"] = "ORANGE",
        ["FREE"] = "FREEMONEY",
        ["FREE_MONEY"] = "FREEMONEY",
        ["MPS"] = "MPESA",
        ["M-PESA"] = "MPESA",
        ["VODAFONE"] = "TELECEL", // Ghana Telecel (ex-Vodafone)
        ["AIRTELTIGO"] = "AIRTEL",
        ["MOOV"] = "MOBICASH", // BF / legacy
    };

    public static NetworkOption? Find(string countryCode, string network)
    {
        var normalized = NormalizeNetwork(network);
        return All.FirstOrDefault(x =>
            x.CountryCode.Equals(countryCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.Network.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<NetworkOption> ForCountry(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return All;
        return All.Where(x => x.CountryCode.Equals(countryCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static IReadOnlyList<MobileMoneyCountryDto> ListCountries() =>
        All
            .GroupBy(x => new { x.CountryCode, x.CountryName, x.Currency, x.PhoneCountryCode })
            .Select(g => new MobileMoneyCountryDto(
                g.Key.CountryCode,
                g.Key.CountryName,
                g.Key.Currency,
                g.Key.PhoneCountryCode,
                g.Select(n => new MobileMoneyNetworkOptionDto(n.Network, n.NetworkLabel)).ToList()))
            .OrderBy(x => x.CountryName)
            .ToList();

    public static string NormalizeNetwork(string network)
    {
        var raw = network.Trim().ToUpperInvariant().Replace(' ', '_');
        return NetworkAliases.TryGetValue(raw, out var alias) ? alias : raw;
    }

    /// <summary>Sépare indicatif pays et numéro national (sans le 0 initial).</summary>
    public static (string CountryDial, string NationalNumber) SplitPhone(string phone, string countryDialCode)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith(countryDialCode, StringComparison.Ordinal))
            digits = digits[countryDialCode.Length..];
        digits = digits.TrimStart('0');
        return (countryDialCode, digits);
    }
}
