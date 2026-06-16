using GiseBsPayGateway.Authentication;

namespace GiseBsPayGateway.Extensions;

public static class HttpContextExtensions
{
    public static ClientApplicationContext GetClientApplicationContext(this HttpContext context)
    {
        if (context.Items.TryGetValue(nameof(ClientApplicationContext), out var value) && value is ClientApplicationContext appContext)
        {
            return appContext;
        }

        throw new UnauthorizedAccessException("Contexte application cliente manquant.");
    }
}
