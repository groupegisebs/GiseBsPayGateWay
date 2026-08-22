using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

/// <summary>
/// Règles Open API MTN MoMo Collections : UUID v4, ISO 4217, codes d'erreur swagger.
/// </summary>
public static class MtnMoMoOpenApi
{
    public const int NoteMaxLength = 160;

    public sealed record ErrorInfo(
        string Code,
        string UserMessage,
        PaymentStatus Status,
        bool IsDuplicate,
        bool RetrySecondaryKey,
        bool KeepPending);

    public static string ToReferenceId(string? idempotencyKey, string internalReference)
    {
        if (Guid.TryParse(idempotencyKey, out var parsed) && IsUuidV4(parsed))
            return parsed.ToString("D");

        var seed = string.IsNullOrWhiteSpace(idempotencyKey) ? internalReference : idempotencyKey;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("mtn-requesttopay:" + seed.Trim()));
        var hex = Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant().ToCharArray();
        hex[12] = '4';
        hex[16] = '8';
        return Guid.ParseExact(new string(hex), "N").ToString("D");
    }

    public static bool IsUuidV4(Guid guid) => guid.ToString("D")[14] == '4';

    public static bool IsDuplicateResource(HttpStatusCode status, string? body) =>
        FromHttp(status, body).IsDuplicate;

    /// <summary>Notes / messages : max 160, pas d'apostrophe ni caractères spéciaux non supportés.</summary>
    public static string SanitizeNote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "TutorSphere";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (c is '\'' or '`' or '"' or '\u2019' or '\u2018')
                continue;
            if (char.IsLetterOrDigit(c) || c is ' ' or '.' or ',' or '-' or ':' or '/' or '#')
                sb.Append(c);
        }

        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length <= NoteMaxLength)
            return cleaned.Length == 0 ? "TutorSphere" : cleaned;
        return cleaned[..NoteMaxLength].Trim();
    }

    public static string ExtractCode(HttpStatusCode status, string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                foreach (var name in new[] { "code", "error", "errorCode", "message" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var prop) &&
                        prop.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(prop.GetString()))
                    {
                        var raw = prop.GetString()!;
                        var known = NormalizeKnownCode(raw);
                        if (known is not null)
                            return known;
                    }
                }
            }
            catch (JsonException)
            {
                // corps non JSON
            }

            var fromBody = NormalizeKnownCode(body);
            if (fromBody is not null)
                return fromBody;
        }

        return status switch
        {
            HttpStatusCode.Conflict => "RESOURCE_ALREADY_EXIST",
            HttpStatusCode.Unauthorized => "ACCESS_DENIED",
            HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
            HttpStatusCode.BadRequest => "BAD_REQUEST",
            HttpStatusCode.Forbidden => "FORBIDDEN_IP",
            HttpStatusCode.ServiceUnavailable => "SERVICE_UNAVAILABLE",
            _ => "INTERNAL_PROCESSING_ERROR"
        };
    }

    public static ErrorInfo FromHttp(HttpStatusCode status, string? body) =>
        FromCode(ExtractCode(status, body));

    public static ErrorInfo FromCode(string? code)
    {
        var key = (code ?? "INTERNAL_PROCESSING_ERROR").Trim().ToUpperInvariant().Replace(' ', '_');
        return key switch
        {
            "RESOURCE_ALREADY_EXIST" or "RESOURCE_ALREADY_EXISTS" =>
                new("RESOURCE_ALREADY_EXIST",
                    "Cette demande existe déjà chez MTN. Vérification du statut.",
                    PaymentStatus.PendingCustomerConfirmation, true, false, true),
            "ACCESS_DENIED" or "ACCESS_DENIED_DUE_TO_INVALID_SUBSCRIPTION_KEY" =>
                new("ACCESS_DENIED",
                    "Clé d'abonnement MTN invalide. Essayez la Secondary Key du produit Collections.",
                    PaymentStatus.Failed, false, true, false),
            "RESOURCE_NOT_FOUND" =>
                new("RESOURCE_NOT_FOUND",
                    "Référence introuvable chez MTN. La demande initiale a-t-elle bien reçu un HTTP 202 ?",
                    PaymentStatus.PendingCustomerConfirmation, false, false, true),
            "BAD_REQUEST" or "REQUEST_REJECTED" =>
                new("BAD_REQUEST",
                    "Requête MTN refusée (UUID v4, devise XAF, notes sans apostrophe, max 160 caractères).",
                    PaymentStatus.Failed, false, false, false),
            "FORBIDDEN_IP" or "FORBIDDEN" =>
                new("FORBIDDEN_IP",
                    "L'adresse IP publique du serveur n'est pas autorisée par MTN. Transmettez-la à votre account manager.",
                    PaymentStatus.Failed, false, false, false),
            "NOT_ALLOWED" =>
                new("NOT_ALLOWED",
                    "Compte API MTN sans permission. Contactez votre account manager MTN.",
                    PaymentStatus.Failed, false, false, false),
            "NOT_ALLOWED_TARGET_ENVIRONMENT" =>
                new("NOT_ALLOWED_TARGET_ENVIRONMENT",
                    "X-Target-Environment incorrect. Cameroun = mtncameroon, tests = sandbox.",
                    PaymentStatus.Failed, false, false, false),
            "INVALID_CALLBACK_URL_HOST" =>
                new("INVALID_CALLBACK_URL_HOST",
                    "L'hôte du callback ne correspond pas à celui configuré sur l'API User MTN (nom d'hôte, pas d'IP).",
                    PaymentStatus.Failed, false, false, false),
            "INVALID_CURRENCY" =>
                new("INVALID_CURRENCY",
                    "Devise non supportée sur ce compte MTN. Cameroun = XAF.",
                    PaymentStatus.Failed, false, false, false),
            "SERVICE_UNAVAILABLE" =>
                new("SERVICE_UNAVAILABLE",
                    "Service MTN temporairement indisponible. Réessayez plus tard.",
                    PaymentStatus.PendingCustomerConfirmation, false, false, true),
            "PAYER_NOT_FOUND" =>
                new("PAYER_NOT_FOUND",
                    "Numéro MTN invalide ou non inscrit à Mobile Money. Utilisez l'indicatif 237.",
                    PaymentStatus.Failed, false, false, false),
            "PAYEE_NOT_FOUND" =>
                new("PAYEE_NOT_FOUND",
                    "Compte destinataire MTN introuvable.",
                    PaymentStatus.Failed, false, false, false),
            "COULD_NOT_PERFORM_TRANSACTION" =>
                new("COULD_NOT_PERFORM_TRANSACTION",
                    "Délai d'approbation dépassé (5 minutes). Demandez au parent de réessayer et de confirmer tout de suite.",
                    PaymentStatus.Expired, false, false, false),
            "INTERNAL_PROCESSING_ERROR" =>
                new("INTERNAL_PROCESSING_ERROR",
                    "Paiement refusé. Vérifiez le solde Mobile Money, puis réessayez.",
                    PaymentStatus.Failed, false, false, false),
            _ => new(key,
                "Échec d'initiation MTN MoMo.",
                PaymentStatus.Failed, false, false, false)
        };
    }

    private static string? NormalizeKnownCode(string raw)
    {
        var upper = raw.Trim().ToUpperInvariant();
        string[] codes =
        [
            "RESOURCE_ALREADY_EXIST", "ACCESS_DENIED", "RESOURCE_NOT_FOUND", "REQUEST_REJECTED",
            "BAD_REQUEST", "FORBIDDEN_IP", "NOT_ALLOWED_TARGET_ENVIRONMENT", "NOT_ALLOWED",
            "INVALID_CALLBACK_URL_HOST", "INVALID_CURRENCY", "SERVICE_UNAVAILABLE",
            "INTERNAL_PROCESSING_ERROR", "PAYER_NOT_FOUND", "PAYEE_NOT_FOUND",
            "COULD_NOT_PERFORM_TRANSACTION"
        ];
        foreach (var code in codes)
        {
            if (upper.Contains(code, StringComparison.Ordinal))
                return code;
        }

        if (upper.Contains("INVALID SUBSCRIPTION KEY", StringComparison.Ordinal))
            return "ACCESS_DENIED";
        return null;
    }
}
