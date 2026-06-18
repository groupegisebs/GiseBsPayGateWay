using Stripe;

namespace GiseBsPayGateway.Services;

public static class StripeErrorMessages
{
    public static string Format(StripeException exception)
    {
        var stripeMessage = exception.StripeError?.Message?.Trim();
        if (!string.IsNullOrWhiteSpace(stripeMessage))
        {
            return $"Stripe : {stripeMessage}";
        }

        return $"Stripe : {exception.Message}";
    }

    public static bool IsResourceMissing(StripeException exception) =>
        string.Equals(exception.StripeError?.Code, "resource_missing", StringComparison.OrdinalIgnoreCase);
}
