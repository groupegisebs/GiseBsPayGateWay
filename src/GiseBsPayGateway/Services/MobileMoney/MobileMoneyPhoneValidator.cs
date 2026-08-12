using System.Text.RegularExpressions;
using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

public static partial class MobileMoneyPhoneValidator
{
    /// <summary>Normalise un numéro camerounais vers 2376xxxxxxxx (sans +).</summary>
    public static bool TryNormalizeCameroonPhone(string? input, out string normalized, out string masked)
    {
        normalized = string.Empty;
        masked = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var digits = DigitsOnly().Replace(input, "");
        if (digits.StartsWith("00237"))
            digits = digits[5..];
        else if (digits.StartsWith("237"))
            digits = digits[3..];

        // Mobile CM : 6XXXXXXXX (9 digits)
        if (digits.Length != 9 || digits[0] != '6' || !digits.All(char.IsDigit))
            return false;

        normalized = "237" + digits;
        masked = $"+237 {digits[0]}** *** {digits[^3..]}";
        return true;
    }

    public static string NormalizeChannel(string? channel) =>
        (channel ?? "").Trim().ToUpperInvariant() switch
        {
            "ORANGE" or "ORANGE_MONEY" or "OM" => "ORANGE",
            "MTN" or "MTN_MOMO" or "MOMO" => "MTN",
            _ => ""
        };

    public static PaymentStatus MapCamPayStatus(string? status) =>
        (status ?? "").Trim().ToUpperInvariant() switch
        {
            "SUCCESSFUL" or "SUCCESS" => PaymentStatus.Succeeded,
            "FAILED" or "FAIL" => PaymentStatus.Failed,
            "PENDING" => PaymentStatus.PendingCustomerConfirmation,
            "EXPIRED" => PaymentStatus.Expired,
            "CANCELLED" or "CANCELED" => PaymentStatus.Cancelled,
            _ => PaymentStatus.RequiresReview
        };

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnly();
}
