using GiseBsPayGateway.Constants;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services.Tax;

namespace GiseBsPayGateway.Services;

/// <summary>
/// Stripe Tax checkout session options when the calling app pre-fills billing address.
/// Stripe Tax ne couvre pas le XAF/XOF ni la TVA Afrique (gérée par le catalogue Pay Gateway).
/// </summary>
public static class StripeCheckoutTaxOptions
{
    public const string BillingAddressAuto = "auto";
    public const string BillingAddressRequired = "required";
    public const string CustomerUpdateNever = "never";

    public static bool UsesAutomaticTax(string? currency, string? billingCountry)
    {
        if (!string.IsNullOrWhiteSpace(currency) && CatalogOptions.IsZeroDecimalCurrency(currency))
            return false;

        var code = billingCountry?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(code) && AfricanTaxRates.ByCountry.ContainsKey(code))
            return false;

        return true;
    }

    public static (string BillingAddressCollection, string? CustomerUpdateAddress) Resolve(
        bool hasPrefilledBillingAddress,
        CustomerUpdateDto? customerUpdate,
        bool automaticTaxEnabled = true)
    {
        if (!automaticTaxEnabled)
            return (BillingAddressAuto, null);

        if (hasPrefilledBillingAddress)
        {
            return (BillingAddressAuto, CustomerUpdateNever);
        }

        // Sans adresse préremplie : collecter l'adresse au checkout et la sauver sur le Customer
        // (requis par Stripe Automatic Tax).
        var addressUpdate = customerUpdate?.Address?.Trim();
        if (string.IsNullOrWhiteSpace(addressUpdate))
            addressUpdate = BillingAddressAuto;
        return (BillingAddressRequired, addressUpdate);
    }
}
