using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;

namespace GiseBsPayGateway.Tests.Services;

public class StripeCheckoutTaxOptionsTests
{
    [Fact]
    public void Resolve_AvecAdressePrefill_UtiliseAutoEtNever()
    {
        var (collection, update) = StripeCheckoutTaxOptions.Resolve(
            hasPrefilledBillingAddress: true,
            customerUpdate: new CustomerUpdateDto("auto"));

        Assert.Equal(StripeCheckoutTaxOptions.BillingAddressAuto, collection);
        Assert.Equal(StripeCheckoutTaxOptions.CustomerUpdateNever, update);
    }

    [Fact]
    public void Resolve_SansAdresse_UtiliseRequired()
    {
        var (collection, update) = StripeCheckoutTaxOptions.Resolve(
            hasPrefilledBillingAddress: false,
            customerUpdate: null);

        Assert.Equal(StripeCheckoutTaxOptions.BillingAddressRequired, collection);
        Assert.Equal(StripeCheckoutTaxOptions.BillingAddressAuto, update);
    }

    [Fact]
    public void Resolve_SansAdresseAvecCustomerUpdateAuto_ConserveAuto()
    {
        var (collection, update) = StripeCheckoutTaxOptions.Resolve(
            hasPrefilledBillingAddress: false,
            customerUpdate: new CustomerUpdateDto("auto"));

        Assert.Equal(StripeCheckoutTaxOptions.BillingAddressRequired, collection);
        Assert.Equal("auto", update);
    }

    [Fact]
    public void UsesAutomaticTax_XafOuPaysAfricain_Desactive()
    {
        Assert.False(StripeCheckoutTaxOptions.UsesAutomaticTax("xaf", "CM"));
        Assert.False(StripeCheckoutTaxOptions.UsesAutomaticTax("XAF", null));
        Assert.False(StripeCheckoutTaxOptions.UsesAutomaticTax("cad", "SN"));
        Assert.True(StripeCheckoutTaxOptions.UsesAutomaticTax("cad", "CA"));
        Assert.True(StripeCheckoutTaxOptions.UsesAutomaticTax("eur", "FR"));
    }

    [Fact]
    public void Resolve_SansTaxeAutomatique_NeForcePasLadresse()
    {
        var (collection, update) = StripeCheckoutTaxOptions.Resolve(
            hasPrefilledBillingAddress: false,
            customerUpdate: null,
            automaticTaxEnabled: false);

        Assert.Equal(StripeCheckoutTaxOptions.BillingAddressAuto, collection);
        Assert.Null(update);
    }
}
