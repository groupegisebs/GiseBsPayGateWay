using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GiseBsPayGateway.Services.Tax;

public interface IAfricanTaxService
{
    /// <summary>
    /// Calcule HT / taxe / TTC pour un pays africain.
    /// <paramref name="netAmountExclusive"/> = montant catalogue hors taxe.
    /// Taux 0 = exonéré → TTC = HT.
    /// </summary>
    AfricanTaxBreakdown Calculate(decimal netAmountExclusive, string currency, string countryCode);

    IReadOnlyList<AfricanTaxRateDto> ListRates();

    bool TryGetRate(string? countryCode, out AfricanTaxRateDto rate);

    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    Task UpdateRateAsync(
        string countryCode,
        decimal ratePercent,
        string? notes,
        CancellationToken cancellationToken = default);

    Task RestoreStandardRateAsync(string countryCode, CancellationToken cancellationToken = default);
}

public sealed record AfricanTaxBreakdown(
    string CountryCode,
    string CountryName,
    string TaxName,
    decimal TaxRatePercent,
    decimal AmountExclusive,
    decimal TaxAmount,
    decimal AmountInclusive,
    string Currency);

public sealed record AfricanTaxRateDto(
    string CountryCode,
    string CountryName,
    string TaxName,
    decimal RatePercent,
    string? Notes,
    decimal StandardRatePercent = 0);

public sealed class AfricanTaxService : IAfricanTaxService
{
    private const string CacheKey = "african-tax-rates-v1";

    private readonly ApplicationDbContext? _db;
    private readonly IMemoryCache? _cache;

    /// <summary>Constructeur tests / fallback catalogue statique.</summary>
    public AfricanTaxService()
    {
    }

    public AfricanTaxService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public AfricanTaxBreakdown Calculate(decimal netAmountExclusive, string currency, string countryCode)
    {
        if (netAmountExclusive < 0)
            throw new InvalidOperationException("Le montant HT ne peut pas être négatif.");

        if (!TryResolve(countryCode, out var rate))
            throw new InvalidOperationException(
                $"Pays '{countryCode}' non supporté pour le calcul de taxe Afrique. " +
                "Fournissez un code ISO 3166-1 alpha-2 africain (ex. CM, SN, CI, NG).");

        var currencyNorm = (currency ?? "XAF").Trim().ToUpperInvariant();
        var isZeroDecimal = CatalogOptions.IsZeroDecimalCurrency(currencyNorm);

        var exclusive = RoundMoney(netAmountExclusive, isZeroDecimal);
        var tax = RoundMoney(exclusive * rate.RatePercent / 100m, isZeroDecimal);
        var inclusive = RoundMoney(exclusive + tax, isZeroDecimal);

        return new AfricanTaxBreakdown(
            rate.CountryCode,
            rate.CountryName,
            rate.TaxName,
            rate.RatePercent,
            exclusive,
            tax,
            inclusive,
            currencyNorm);
    }

    public IReadOnlyList<AfricanTaxRateDto> ListRates() =>
        LoadRates()
            .Values
            .OrderBy(r => r.CountryName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool TryGetRate(string? countryCode, out AfricanTaxRateDto rate) =>
        TryResolve(countryCode, out rate);

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (_db is null)
            return;

        var existing = await _db.AfricanTaxRateSettings
            .Select(x => x.CountryCode)
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var toAdd = AfricanTaxRates.AllOrdered()
            .Where(r => !existingSet.Contains(r.CountryCode))
            .Select(r => new AfricanTaxRateSetting
            {
                CountryCode = r.CountryCode,
                CountryNameFr = r.CountryNameFr,
                TaxName = r.TaxName,
                RatePercent = r.RatePercent,
                StandardRatePercent = r.PublishedStandardRate,
                Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes
            })
            .ToList();

        if (toAdd.Count == 0)
            return;

        _db.AfricanTaxRateSettings.AddRange(toAdd);
        await _db.SaveChangesAsync(cancellationToken);
        InvalidateCache();
    }

    public async Task UpdateRateAsync(
        string countryCode,
        decimal ratePercent,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (_db is null)
            throw new InvalidOperationException("Mise à jour des taux indisponible hors contexte base de données.");

        if (ratePercent < 0 || ratePercent > 100)
            throw new InvalidOperationException("Le taux doit être entre 0 et 100 % (0 = exonéré).");

        var code = NormalizeCode(countryCode)
            ?? throw new InvalidOperationException("Code pays requis.");

        var entity = await _db.AfricanTaxRateSettings
            .FirstOrDefaultAsync(x => x.CountryCode == code, cancellationToken)
            ?? throw new InvalidOperationException($"Taux introuvable pour le pays '{code}'.");

        entity.RatePercent = decimal.Round(ratePercent, 4, MidpointRounding.AwayFromZero);
        if (notes is not null)
            entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        InvalidateCache();
    }

    public async Task RestoreStandardRateAsync(string countryCode, CancellationToken cancellationToken = default)
    {
        if (_db is null)
            throw new InvalidOperationException("Mise à jour des taux indisponible hors contexte base de données.");

        var code = NormalizeCode(countryCode)
            ?? throw new InvalidOperationException("Code pays requis.");

        var entity = await _db.AfricanTaxRateSettings
            .FirstOrDefaultAsync(x => x.CountryCode == code, cancellationToken)
            ?? throw new InvalidOperationException($"Taux introuvable pour le pays '{code}'.");

        entity.RatePercent = entity.StandardRatePercent;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        InvalidateCache();
    }

    private bool TryResolve(string? countryCode, out AfricanTaxRateDto rate)
    {
        rate = null!;
        var code = NormalizeCode(countryCode);
        if (code is null)
            return false;

        if (LoadRates().TryGetValue(code, out rate!))
            return true;

        if (AfricanTaxRates.TryGet(code, out var fallback))
        {
            rate = ToDto(fallback);
            return true;
        }

        return false;
    }

    private IReadOnlyDictionary<string, AfricanTaxRateDto> LoadRates()
    {
        if (_db is null || _cache is null)
            return AfricanTaxRates.ByCountry.ToDictionary(
                kv => kv.Key,
                kv => ToDto(kv.Value),
                StringComparer.OrdinalIgnoreCase);

        return _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var rows = _db.AfricanTaxRateSettings.AsNoTracking().ToList();
            if (rows.Count == 0)
            {
                return AfricanTaxRates.ByCountry.ToDictionary(
                    kv => kv.Key,
                    kv => ToDto(kv.Value),
                    StringComparer.OrdinalIgnoreCase);
            }

            return rows
                .GroupBy(r => r.CountryCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var r = g.First();
                        return new AfricanTaxRateDto(
                            r.CountryCode,
                            r.CountryNameFr,
                            r.TaxName,
                            r.RatePercent,
                            r.Notes,
                            r.StandardRatePercent);
                    },
                    StringComparer.OrdinalIgnoreCase);
        })!;
    }

    private void InvalidateCache() => _cache?.Remove(CacheKey);

    private static AfricanTaxRateDto ToDto(AfricanTaxRates.Rate rate) =>
        new(rate.CountryCode, rate.CountryNameFr, rate.TaxName, rate.RatePercent, rate.Notes, rate.PublishedStandardRate);

    private static string? NormalizeCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return null;
        return countryCode.Trim().ToUpperInvariant();
    }

    private static decimal RoundMoney(decimal value, bool zeroDecimal) =>
        zeroDecimal
            ? Math.Round(value, 0, MidpointRounding.AwayFromZero)
            : Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
