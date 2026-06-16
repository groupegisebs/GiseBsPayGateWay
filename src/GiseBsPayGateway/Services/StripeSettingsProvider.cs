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
    bool FromServerFile);

public interface IStripeSettingsProvider
{
    Task<StripeSettingsSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default);
    bool IsConfiguredFromServerFile { get; }
}

public class StripeSettingsProvider : IStripeSettingsProvider
{
    private readonly ApplicationDbContext _db;
    private readonly StripeSecretsOptions _fileOptions;

    public StripeSettingsProvider(ApplicationDbContext db, IOptions<StripeSecretsOptions> fileOptions)
    {
        _db = db;
        _fileOptions = fileOptions.Value;
    }

    public bool IsConfiguredFromServerFile =>
        !string.IsNullOrWhiteSpace(_fileOptions.SecretKey);

    public async Task<StripeSettingsSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (IsConfiguredFromServerFile)
        {
            return new StripeSettingsSnapshot(
                _fileOptions.PublishableKey.Trim(),
                _fileOptions.SecretKey.Trim(),
                _fileOptions.WebhookSecret.Trim(),
                _fileOptions.IsLiveMode,
                FromServerFile: true);
        }

        var settings = await _db.StripeSettings.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            return null;
        }

        return new StripeSettingsSnapshot(
            settings.PublishableKey,
            settings.SecretKey,
            settings.WebhookSecret,
            settings.IsLiveMode,
            FromServerFile: false);
    }
}
