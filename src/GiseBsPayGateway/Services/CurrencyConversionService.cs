using GiseBsPayGateway.Constants;

namespace GiseBsPayGateway.Services;

public interface ICurrencyConversionService
{
    Task<decimal> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default);
}

public class CurrencyConversionService : ICurrencyConversionService
{
    private readonly IExchangeRateProvider _rates;

    public CurrencyConversionService(IExchangeRateProvider rates)
    {
        _rates = rates;
    }

    public async Task<decimal> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        var from = CatalogOptions.ResolveCurrency(fromCurrency);
        var to = CatalogOptions.ResolveCurrency(toCurrency);

        if (from == to)
        {
            return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        }

        var rates = await _rates.GetRatesToCadAsync(cancellationToken);

        if (!rates.TryGetValue(from, out var fromRate) || fromRate <= 0)
        {
            throw new InvalidOperationException(
                $"Taux de conversion manquant pour '{from.ToUpperInvariant()}'.");
        }

        if (!rates.TryGetValue(to, out var toRate) || toRate <= 0)
        {
            throw new InvalidOperationException(
                $"Taux de conversion manquant pour '{to.ToUpperInvariant()}'.");
        }

        var amountInCad = amount * fromRate;
        var converted = amountInCad / toRate;
        return decimal.Round(converted, 2, MidpointRounding.AwayFromZero);
    }
}
