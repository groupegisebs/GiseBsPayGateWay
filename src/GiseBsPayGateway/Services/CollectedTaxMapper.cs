using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using Stripe;
using Stripe.Checkout;

namespace GiseBsPayGateway.Services;

public static class CollectedTaxMapper
{
    public static IReadOnlyList<CollectedTaxLine> MapFromCheckoutSession(Session session)
    {
        var taxes = session.TotalDetails?.Breakdown?.Taxes;
        if (taxes is { Count: > 0 })
        {
            return MapFromSessionBreakdownTaxes(taxes, ResolveCountry(session), ResolveState(session));
        }

        if (session.TotalDetails?.AmountTax is > 0)
        {
            var country = ResolveCountry(session) ?? "XX";
            var state = ResolveState(session);
            var amount = session.TotalDetails.AmountTax / 100m;
            var subtotal = session.AmountSubtotal is > 0 ? session.AmountSubtotal.Value / 100m : 0m;
            var rate = subtotal > 0 ? Math.Round(amount / subtotal, 6, MidpointRounding.AwayFromZero) : 0m;

            return
            [
                new CollectedTaxLine
                {
                    SortOrder = 0,
                    Code = $"{country}_tax".ToLowerInvariant(),
                    Name = "TAX",
                    Rate = rate,
                    Amount = amount,
                    Type = ResolveComponentType(country, "tax", state)
                }
            ];
        }

        return [];
    }

    public static IReadOnlyList<CollectedTaxLine> MapFromStripeInvoice(Invoice invoice)
    {
        if (invoice.TotalTaxes is { Count: > 0 })
        {
            var country = invoice.CustomerAddress?.Country ?? "XX";
            var state = invoice.CustomerAddress?.State;
            var lines = new List<CollectedTaxLine>();
            var sortOrder = 0;

            foreach (var tax in invoice.TotalTaxes)
            {
                var rateDetails = tax.TaxRateDetails;
                var rate = rateDetails?.TaxRate?.Percentage is decimal pct && pct > 0
                    ? pct / 100m
                    : 0m;
                var taxType = NormalizeTaxType(rateDetails?.TaxRate?.TaxType ?? tax.Type);
                var code = $"{country}_{taxType}".ToLowerInvariant();

                lines.Add(new CollectedTaxLine
                {
                    SortOrder = sortOrder++,
                    Code = code,
                    Name = rateDetails?.TaxRate?.DisplayName ?? taxType.ToUpperInvariant(),
                    Rate = rate,
                    Amount = tax.Amount / 100m,
                    Type = ResolveComponentType(country, taxType, state)
                });
            }

            return lines;
        }

        if (invoice.Total is long total && invoice.TotalExcludingTax is long totalExcludingTax
            && total > totalExcludingTax)
        {
            var country = invoice.CustomerAddress?.Country ?? "XX";
            var state = invoice.CustomerAddress?.State;
            var amount = (total - totalExcludingTax) / 100m;
            var subtotal = totalExcludingTax / 100m;
            var rate = subtotal > 0 ? Math.Round(amount / subtotal, 6, MidpointRounding.AwayFromZero) : 0m;

            return
            [
                new CollectedTaxLine
                {
                    SortOrder = 0,
                    Code = $"{country}_tax".ToLowerInvariant(),
                    Name = "TAX",
                    Rate = rate,
                    Amount = amount,
                    Type = ResolveComponentType(country, "tax", state)
                }
            ];
        }

        return [];
    }

    public static void ApplyBillingAddress(CollectedTaxRecord record, Address? address)
    {
        if (address is null)
        {
            return;
        }

        record.BillingLine1 = address.Line1;
        record.BillingLine2 = address.Line2;
        record.BillingCity = address.City;
        record.BillingState = address.State;
        record.BillingPostalCode = address.PostalCode;
        record.BillingCountry = address.Country?.Trim().ToUpperInvariant();
    }

    public static BillingAddressDto? ToBillingAddressDto(CollectedTaxRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.BillingLine1)
            || string.IsNullOrWhiteSpace(record.BillingCity)
            || string.IsNullOrWhiteSpace(record.BillingPostalCode)
            || string.IsNullOrWhiteSpace(record.BillingCountry))
        {
            return null;
        }

        return new BillingAddressDto(
            record.BillingLine1,
            record.BillingLine2,
            record.BillingCity,
            record.BillingState,
            record.BillingPostalCode,
            record.BillingCountry);
    }

    public static IReadOnlyList<CollectedTaxLineDto> ToLineDtos(IEnumerable<CollectedTaxLine> lines) =>
        lines.OrderBy(x => x.SortOrder)
            .Select(x => new CollectedTaxLineDto(x.Code, x.Name, x.Rate, x.Amount, x.Type))
            .ToList();

    private static List<CollectedTaxLine> MapFromSessionBreakdownTaxes(
        IEnumerable<SessionTotalDetailsBreakdownTax> taxes,
        string? country,
        string? state)
    {
        var normalizedCountry = (country ?? "XX").Trim().ToUpperInvariant();
        var lines = new List<CollectedTaxLine>();
        var sortOrder = 0;
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tax in taxes)
        {
            if (tax.Amount is not > 0)
            {
                continue;
            }

            var amount = tax.Amount / 100m;

            var rateObj = tax.Rate;
            var rate = rateObj?.Percentage is decimal pct && pct > 0
                ? pct / 100m
                : tax.TaxableAmount is > 0
                    ? Math.Round(amount / (tax.TaxableAmount.Value / 100m), 6, MidpointRounding.AwayFromZero)
                    : 0m;

            var taxType = NormalizeTaxType(rateObj?.TaxType);
            var code = $"{normalizedCountry}_{taxType}".ToLowerInvariant();
            if (!seenCodes.Add(code))
            {
                code = $"{code}_{sortOrder}";
            }

            lines.Add(new CollectedTaxLine
            {
                SortOrder = sortOrder++,
                Code = code,
                Name = rateObj?.DisplayName ?? taxType.ToUpperInvariant(),
                Rate = rate,
                Amount = amount,
                Type = ResolveComponentType(normalizedCountry, taxType, state ?? rateObj?.State)
            });
        }

        return lines;
    }

    private static string? ResolveCountry(Session session) =>
        session.CustomerDetails?.Address?.Country?.Trim().ToUpperInvariant();

    private static string? ResolveState(Session session) =>
        session.CustomerDetails?.Address?.State?.Trim();

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
