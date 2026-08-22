using System.Net;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services.MobileMoney;

namespace GiseBsPayGateway.Tests.Services;

public class MtnMoMoOpenApiTests
{
    [Fact]
    public void ToReferenceId_parses_uuid_v4()
    {
        var id = "c72025f5-5cd1-4630-99e4-8ba4722fad56";
        Assert.Equal(id, MtnMoMoOpenApi.ToReferenceId(id, "PAY-1"));
        Assert.True(MtnMoMoOpenApi.IsUuidV4(Guid.Parse(id)));
    }

    [Fact]
    public void ToReferenceId_is_stable_uuid_v4()
    {
        var a = MtnMoMoOpenApi.ToReferenceId("idem-abc", "PAY-1");
        var b = MtnMoMoOpenApi.ToReferenceId("idem-abc", "PAY-99");
        Assert.True(Guid.TryParse(a, out var g));
        Assert.True(MtnMoMoOpenApi.IsUuidV4(g));
        Assert.Equal(a, b);
    }

    [Fact]
    public void SanitizeNote_strips_apostrophe_and_caps_160()
    {
        var cleaned = MtnMoMoOpenApi.SanitizeNote("Paiement d'essai 'urgent'");
        Assert.DoesNotContain('\'', cleaned);
        Assert.Contains("essai", cleaned);

        var longNote = new string('a', 200);
        Assert.Equal(160, MtnMoMoOpenApi.SanitizeNote(longNote).Length);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, """{"code":"RESOURCE_ALREADY_EXIST"}""", "RESOURCE_ALREADY_EXIST", true)]
    [InlineData(HttpStatusCode.Unauthorized, "ACCESS DENIED DUE TO INVALID SUBSCRIPTION KEY", "ACCESS_DENIED", false)]
    [InlineData(HttpStatusCode.NotFound, """{"code":"RESOURCE_NOT_FOUND"}""", "RESOURCE_NOT_FOUND", false)]
    [InlineData(HttpStatusCode.BadRequest, """{"code":"PAYER_NOT_FOUND"}""", "PAYER_NOT_FOUND", false)]
    public void FromHttp_maps_swagger_codes(HttpStatusCode http, string body, string expected, bool duplicate)
    {
        var error = MtnMoMoOpenApi.FromHttp(http, body);
        Assert.Equal(expected, error.Code);
        Assert.Equal(duplicate, error.IsDuplicate);
    }

    [Fact]
    public void Internal_processing_error_tells_parent_to_check_balance()
    {
        var error = MtnMoMoOpenApi.FromCode("INTERNAL_PROCESSING_ERROR");
        Assert.Equal(PaymentStatus.Failed, error.Status);
        Assert.Contains("solde", error.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Could_not_perform_transaction_is_five_minute_timeout()
    {
        var error = MtnMoMoOpenApi.FromCode("COULD_NOT_PERFORM_TRANSACTION");
        Assert.Equal(PaymentStatus.Expired, error.Status);
        Assert.Contains("5 minutes", error.UserMessage);
    }

    [Fact]
    public void Secondary_key_retry_flag_on_invalid_subscription()
    {
        Assert.True(MtnMoMoOpenApi.FromCode("ACCESS_DENIED").RetrySecondaryKey);
        Assert.True(MtnMoMoOpenApi.FromHttp(HttpStatusCode.Unauthorized, "invalid subscription key").RetrySecondaryKey);
    }
}
