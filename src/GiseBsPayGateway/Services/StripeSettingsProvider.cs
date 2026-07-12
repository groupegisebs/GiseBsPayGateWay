using GiseBsPayGateway.Data;
using GiseBsPayGateway.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public record StripeSettingsSnapshot(
    string PublishableKey,
    string SecretKey,
    string WebhookSecret,
    bool IsLiveMode,
    bool FromServerFile,
    string Mode);

public interface IStripeSettingsProvider
{
    /// <summary>Secrets pour la requête courante (DEV → test, sinon prod).</summary>
    Task<StripeSettingsSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Tous les jeux de secrets configurés (pour valider les webhooks live + test).</summary>
    Task<IReadOnlyList<StripeSettingsSnapshot>> GetAllConfiguredAsync(CancellationToken cancellationToken = default);

    bool IsConfiguredFromServerFile { get; }
    bool HasTestSecrets { get; }
}

public class StripeSettingsProvider : IStripeSettingsProvider
{
    private readonly ApplicationDbContext _db;
    private readonly StripeSecretsOptions _fileOptions;
    private readonly IStripeEnvironmentAccessor _environment;

    public StripeSettingsProvider(
        ApplicationDbContext db,
        IOptions<StripeSecretsOptions> fileOptions,
        IStripeEnvironmentAccessor environment)
    {
        _db = db;
        _fileOptions = fileOptions.Value;
        _environment = environment;
    }

    public bool IsConfiguredFromServerFile =>
        HasSecret(_fileOptions.SecretKey)
        || HasSecret(_fileOptions.Live.SecretKey)
        || HasSecret(_fileOptions.Test.SecretKey);

    public bool HasTestSecrets => HasSecret(ResolveTestKeys().SecretKey);

    public async Task<StripeSettingsSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (_environment.UseTestMode)
        {
            var test = TryFromFile(ResolveTestKeys(), isLive: false, mode: "DEV");
            if (test is not null)
                return test;

            throw new InvalidOperationException(
                "Mode DEV demandé (header X-Stripe-Env: DEV) mais les secrets Stripe Test ne sont pas configurés. " +
                "Ajoutez Stripe:Test dans secrets.json.");
        }

        var live = TryFromFile(ResolveLiveKeys(), isLive: true, mode: "PROD");
        if (live is not null)
            return live;

        return await GetFromDatabaseAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StripeSettingsSnapshot>> GetAllConfiguredAsync(
        CancellationToken cancellationToken = default)
    {
        var list = new List<StripeSettingsSnapshot>();

        var live = TryFromFile(ResolveLiveKeys(), isLive: true, mode: "PROD");
        if (live is not null)
            list.Add(live);

        var test = TryFromFile(ResolveTestKeys(), isLive: false, mode: "DEV");
        if (test is not null)
            list.Add(test);

        if (list.Count > 0)
            return list;

        var db = await GetFromDatabaseAsync(cancellationToken);
        if (db is not null)
            list.Add(db);

        return list;
    }

    private StripeKeySetOptions ResolveLiveKeys()
    {
        if (HasSecret(_fileOptions.Live.SecretKey))
            return _fileOptions.Live;

        return new StripeKeySetOptions
        {
            PublishableKey = _fileOptions.PublishableKey,
            SecretKey = _fileOptions.SecretKey,
            WebhookSecret = _fileOptions.WebhookSecret
        };
    }

    private StripeKeySetOptions ResolveTestKeys() => _fileOptions.Test;

    private static StripeSettingsSnapshot? TryFromFile(StripeKeySetOptions keys, bool isLive, string mode)
    {
        if (!HasSecret(keys.SecretKey))
            return null;

        return new StripeSettingsSnapshot(
            keys.PublishableKey.Trim(),
            keys.SecretKey.Trim(),
            keys.WebhookSecret?.Trim() ?? string.Empty,
            isLive,
            FromServerFile: true,
            mode);
    }

    private async Task<StripeSettingsSnapshot?> GetFromDatabaseAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.StripeSettings.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
            return null;

        return new StripeSettingsSnapshot(
            settings.PublishableKey,
            settings.SecretKey,
            settings.WebhookSecret,
            settings.IsLiveMode,
            FromServerFile: false,
            settings.IsLiveMode ? "PROD" : "DEV");
    }

    private static bool HasSecret(string? secret) => !string.IsNullOrWhiteSpace(secret);
}
