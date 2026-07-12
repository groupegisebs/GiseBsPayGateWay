namespace GiseBsPayGateway.Services;

/// <summary>
/// Mode Stripe de la requête courante.
/// Header <c>X-Stripe-Env: DEV</c> → secrets test ; sinon → production.
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

public interface IStripeEnvironmentAccessor
{
    /// <summary>True si la requête demande le bac à sable (X-Stripe-Env: DEV).</summary>
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
            var header = _httpContextAccessor.HttpContext?.Request.Headers[StripeEnvironment.HeaderName].FirstOrDefault();
            return StripeEnvironment.IsDevRequest(header);
        }
    }
}
