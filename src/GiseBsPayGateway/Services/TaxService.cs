using GiseBsPayGateway.DTOs;
using Stripe;
using Stripe.Tax;

namespace GiseBsPayGateway.Services;

public interface ITaxService
{
    Task<TaxCalculationResponse> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default);
}

public sealed class TaxCalculationException : Exception
{
    public TaxCalculationException(string message) : base(message)
    {
    }
}

public class TaxService : ITaxService
{
    private const long DefaultAmountMinorUnits = 10000;
    private const string DefaultCurrency = "eur";

    private static readonly HashSet<string> StateRequiredCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "CA", "US"
    };

    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly ILogger<TaxService> _logger;

    public TaxService(IStripeSettingsProvider stripeSettings, ILogger<TaxService> logger)
    {
        _stripeSettings = stripeSettings;
        _logger = logger;
    }

    public async Task<TaxCalculationResponse> CalculateAsync(
        TaxCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var formattedAddress = StripeAddressFormatter.Format(request.BillingAddress);
        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? DefaultCurrency
            : request.Currency.Trim().ToLowerInvariant();
        var amountMinorUnits = request.AmountMinorUnits is > 0
            ? request.AmountMinorUnits.Value
            : DefaultAmountMinorUnits;

        await ConfigureStripeAsync(cancellationToken);

        Calculation calculation;
        try
        {
            var service = new CalculationService();
            calculation = await service.CreateAsync(new CalculationCreateOptions
            {
                Currency = currency,
                CustomerDetails = new CalculationCustomerDetailsOptions
                {
                    Address = new AddressOptions
                    {
                        Line1 = formattedAddress.Line1,
                        Line2 = formattedAddress.Line2,
                        City = formattedAddress.City,
                        State = formattedAddress.State,
                        PostalCode = formattedAddress.PostalCode,
                        Country = formattedAddress.Country
                    },
                    AddressSource = "billing"
                },
                LineItems =
                [
                    new CalculationLineItemOptions
                    {
                        Amount = amountMinorUnits,
                        Reference = "tax-estimate",
                        TaxCode = StripeTaxDefaults.DigitalProductTaxCode,
                        TaxBehavior = StripeTaxDefaults.PriceTaxBehaviorExclusive
                    }
                ]
            }, cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe Tax Calculation API a refusé l'adresse {Country}/{State}",
                formattedAddress.Country, formattedAddress.State);
            throw new TaxCalculationException("Impossible de calculer les taxes pour cette adresse.");
        }

        var mapped = TaxCalculationMapper.Map(calculation, formattedAddress);
        if (mapped is null)
        {
            throw new TaxCalculationException("Impossible de calculer les taxes pour cette adresse.");
        }

        return mapped;
    }

    private static void ValidateRequest(TaxCalculationRequest request)
    {
        var address = request.BillingAddress;

        if (string.IsNullOrWhiteSpace(address.Line1)
            || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.PostalCode)
            || string.IsNullOrWhiteSpace(address.Country))
        {
            throw new TaxCalculationException("Adresse de facturation incomplète.");
        }

        var country = address.Country.Trim().ToUpperInvariant();
        if (StateRequiredCountries.Contains(country) && string.IsNullOrWhiteSpace(address.State))
        {
            throw new TaxCalculationException("Province ou état requis pour cette adresse.");
        }
    }

    private async Task ConfigureStripeAsync(CancellationToken cancellationToken)
    {
        var settings = await _stripeSettings.GetActiveAsync(cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException(
                "Stripe n'est pas configuré. Créez /opt/apps/gisebs-pay-gateway/secrets.json sur le serveur ou configurez les clés dans l'admin.");
        }

        StripeConfiguration.ApiKey = settings.SecretKey;
    }
}
