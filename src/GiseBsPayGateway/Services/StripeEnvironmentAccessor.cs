namespace GiseBsPayGateway.Services;

/// <summary>
/// Mode Stripe de la requête courante.
/// Header <c>X-Stripe-Env: DEV</c> → secrets test ; sinon → production.
/// Les webhooks Stripe n'envoient pas ce header : utiliser <see cref="StripeEnvironmentScope"/>.
/// </summary>
public static class StripeEnvironment
{
    public const string HeaderName = "X-Stripe-Env";
    public const string DevValue = "DEV";
    public const string TestValue = "TEST";

    public static bool IsDevRequest(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        var value = headerValue.Trim();
        return value.Equals(DevValue, StringComparison.OrdinalIgnoreCase)
               || value.Equals(TestValue, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Force le mode Stripe pour le flux async courant (webhooks, jobs).
/// </summary>
public static class StripeEnvironmentScope
{
    private static readonly AsyncLocal<bool?> OverrideUseTestMode = new();

    public static bool? CurrentUseTestMode => OverrideUseTestMode.Value;

    public static IDisposable Begin(bool useTestMode)
    {
        var previous = OverrideUseTestMode.Value;
        OverrideUseTestMode.Value = useTestMode;
        return new Scope(() => OverrideUseTestMode.Value = previous);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            onDispose();
        }
    }
}

public interface IStripeEnvironmentAccessor
{
    /// <summary>True si la requête demande le bac à sable (X-Stripe-Env: DEV) ou si un scope webhook/job l'impose.</summary>
    bool UseTestMode { get; }
}

public class HttpStripeEnvironmentAccessor : IStripeEnvironmentAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpStripeEnvironmentAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool UseTestMode
    {
        get
        {
            if (StripeEnvironmentScope.CurrentUseTestMode is { } scoped)
                return scoped;

            var header = _httpContextAccessor.HttpContext?.Request.Headers[StripeEnvironment.HeaderName].FirstOrDefault();
            return StripeEnvironment.IsDevRequest(header);
        }
    }
}
