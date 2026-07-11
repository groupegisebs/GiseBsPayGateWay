using System.Globalization;
using System.Text.Json;
using GiseBsPayGateway.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public interface IExchangeRateProvider
{
    /// <summary>Taux : 1 unité de devise = valeur CAD.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetRatesToCadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Taux quotidiens Banque du Canada (Valet API), avec cache et repli config.</summary>
public class BankOfCanadaExchangeRateProvider : IExchangeRateProvider
{
    private const string CacheKey = "boc:fx-rates-to-cad";

    private static readonly IReadOnlyDictionary<string, string> SeriesByCurrency =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["usd"] = "FXUSDCAD",
            ["eur"] = "FXEURCAD",
            ["gbp"] = "FXGBPCAD",
            ["chf"] = "FXCHFCAD"
        };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly CurrencyConversionOptions _options;
    private readonly ILogger<BankOfCanadaExchangeRateProvider> _logger;

    public BankOfCanadaExchangeRateProvider(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<CurrencyConversionOptions> options,
        ILogger<BankOfCanadaExchangeRateProvider> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetRatesToCadAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, decimal>? cached) && cached is not null)
        {
            return cached;
        }

        Dictionary<string, decimal> rates;
        if (_options.UseLiveRates)
        {
            try
            {
                rates = await FetchLiveRatesAsync(cancellationToken);
                _logger.LogInformation(
                    "Taux BoC chargés : {Rates}",
                    string.Join(", ", rates.Select(kv => $"{kv.Key.ToUpperInvariant()}={kv.Value}")));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Échec récupération taux BoC — repli sur RatesToCad configurés.");
                rates = BuildFallbackRates();
            }
        }
        else
        {
            rates = BuildFallbackRates();
        }

        var cacheMinutes = Math.Clamp(_options.LiveRatesCacheMinutes, 5, 24 * 60);
        _cache.Set(CacheKey, (IReadOnlyDictionary<string, decimal>)rates, TimeSpan.FromMinutes(cacheMinutes));
        return rates;
    }

    private async Task<Dictionary<string, decimal>> FetchLiveRatesAsync(CancellationToken cancellationToken)
    {
        await using var stream = await _httpClient.GetStreamAsync(_options.BankOfCanadaUrl, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("observations", out var observations) ||
            observations.ValueKind != JsonValueKind.Array ||
            observations.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Réponse Banque du Canada sans observations.");
        }

        var observation = observations[observations.GetArrayLength() - 1];
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["cad"] = 1m
        };

        foreach (var (currency, series) in SeriesByCurrency)
        {
            if (!observation.TryGetProperty(series, out var seriesNode) ||
                !seriesNode.TryGetProperty("v", out var valueNode))
            {
                continue;
            }

            var raw = valueNode.GetString();
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) && rate > 0)
            {
                rates[currency] = rate;
            }
        }

        // Compléter les devises manquantes depuis le repli config.
        foreach (var (currency, fallbackRate) in _options.RatesToCad)
        {
            if (!rates.ContainsKey(currency) && fallbackRate > 0)
            {
                rates[currency] = fallbackRate;
            }
        }

        if (rates.Count < 2)
        {
            throw new InvalidOperationException("Aucun taux FX utilisable dans la réponse Banque du Canada.");
        }

        return rates;
    }

    private Dictionary<string, decimal> BuildFallbackRates()
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["cad"] = 1m
        };

        foreach (var (currency, rate) in _options.RatesToCad)
        {
            if (rate > 0)
            {
                rates[currency] = rate;
            }
        }

        return rates;
    }
}
