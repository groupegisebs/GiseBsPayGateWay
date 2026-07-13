using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Options;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public interface IPayoutCallbackNotifier
{
    Task NotifyAsync(ClientApplication app, string eventType, object payload, CancellationToken cancellationToken = default);
}

public class PayoutCallbackNotifier(
    IHttpClientFactory httpClientFactory,
    IOptions<PayoutCallbackOptions> options,
    ILogger<PayoutCallbackNotifier> logger) : IPayoutCallbackNotifier
{
    public async Task NotifyAsync(ClientApplication app, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var url = app.WebhookCallbackUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogDebug("Pas de WebhookCallbackUrl pour {AppCode}, callback ignoré ({Event})", app.AppCode, eventType);
            return;
        }

        var envelope = new
        {
            eventType,
            appCode = app.AppCode,
            occurredAt = DateTime.UtcNow,
            data = payload
        };

        var json = JsonSerializer.Serialize(envelope);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var secret = options.Value.SharedSecret;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var signature = ComputeHmac(secret, json);
            request.Headers.TryAddWithoutValidation("X-PayGateway-Signature", signature);
            request.Headers.TryAddWithoutValidation("X-PayGateway-Event", eventType);
        }

        try
        {
            var client = httpClientFactory.CreateClient("PayoutCallback");
            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Callback payout {Event} vers {Url} a échoué: {Status}",
                    eventType, url, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec callback payout {Event} vers {Url}", eventType, url);
        }
    }

    private static string ComputeHmac(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
