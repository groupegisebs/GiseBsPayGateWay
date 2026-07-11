using GiseBsPayGateway.DTOs;

namespace GiseBsPayGateway.Services;

/// <summary>
/// Stripe Tax checkout session options when the calling app pre-fills billing address.
/// </summary>
public static class StripeCheckoutTaxOptions
{
    public const string BillingAddressAuto = "auto";
    public const string BillingAddressRequired = "required";
    public const string CustomerUpdateNever = "never";

    public static (string BillingAddressCollection, string? CustomerUpdateAddress) Resolve(
        bool hasPrefilledBillingAddress,
        CustomerUpdateDto? customerUpdate)
    {
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
