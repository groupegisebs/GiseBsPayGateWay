using System.Text;
using System.Text.Json;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Services.MobileMoney;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace GiseBsPayGateway.Tests.Services;

public class MobileMoneyOrchestratorTests
{
    [Fact]
    public async Task Charge_Orange_Succes_PendingCustomerConfirmation()
    {
        await using var db = TestDbContextFactory.Create(nameof(Charge_Orange_Succes_PendingCustomerConfirmation));
        var (app, orch) = await CreateSutAsync(db);

        var result = await orch.ChargeAsync(app, ChargeRequest(channel: "ORANGE", phone: "+237690000000"), "idem-1");

        Assert.Equal(PaymentStatus.PendingCustomerConfirmation.ToString(), result.Status);
        Assert.Equal("ORANGE", result.Channel);
        Assert.Contains("**", result.PhoneMasked);
        Assert.False(string.IsNullOrWhiteSpace(result.ProviderReference));
        Assert.DoesNotContain("690000000", result.PhoneMasked);
    }

    [Fact]
    public async Task Charge_Mtn_Succes_PuisWebhook_Paid()
    {
        await using var db = TestDbContextFactory.Create(nameof(Charge_Mtn_Succes_PuisWebhook_Paid));
        var (app, orch, sim) = await CreateSutWithSimAsync(db);

        var charge = await orch.ChargeAsync(app, ChargeRequest(channel: "MTN", phone: "+237670000000"), "idem-mtn");
        Assert.Equal(PaymentStatus.PendingCustomerConfirmation.ToString(), charge.Status);

        var payload = JsonSerializer.Serialize(new
        {
            reference = charge.ProviderReference,
            external_reference = charge.PaymentCode,
            status = "SUCCESSFUL",
            amount = 5000,
            currency = "XAF",
            @operator = "MTN"
        });

        var http = CreateHttpRequest(payload);
        var (status, _) = await orch.HandleWebhookAsync("campay", http.Request);
        Assert.Equal(StatusCodes.Status200OK, status);

        var payment = await db.PaymentTransactions.SingleAsync(x => x.PaymentCode == charge.PaymentCode);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.PaidAt);
    }

    [Fact]
    public async Task Charge_SoldeInsuffisant_Failed()
    {
        await using var db = TestDbContextFactory.Create(nameof(Charge_SoldeInsuffisant_Failed));
        var (app, orch) = await CreateSutAsync(db);

        var result = await orch.ChargeAsync(app, ChargeRequest(phone: "+237690000001"), "idem-fail");

        Assert.Equal(PaymentStatus.Failed.ToString(), result.Status);
        var payment = await db.PaymentTransactions.SingleAsync();
        Assert.Equal("INSUFFICIENT_FUNDS", payment.FailureCode);
    }

    [Fact]
    public async Task Charge_NumeroInvalide_Leve()
    {
        await using var db = TestDbContextFactory.Create(nameof(Charge_NumeroInvalide_Leve));
        var (app, orch) = await CreateSutAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.ChargeAsync(app, ChargeRequest(phone: "+33123456789"), "idem-bad"));
    }

    [Fact]
    public async Task Charge_MemeIdempotencyKey_RetourneMemeTentative()
    {
        await using var db = TestDbContextFactory.Create(nameof(Charge_MemeIdempotencyKey_RetourneMemeTentative));
        var (app, orch) = await CreateSutAsync(db);

        var first = await orch.ChargeAsync(app, ChargeRequest(), "same-key");
        var second = await orch.ChargeAsync(app, ChargeRequest(), "same-key");

        Assert.Equal(first.PaymentCode, second.PaymentCode);
        Assert.Equal(1, await db.PaymentTransactions.CountAsync());
    }

    [Fact]
    public async Task Webhook_Duplique_DixFois_UnSeulEffet()
    {
        await using var db = TestDbContextFactory.Create(nameof(Webhook_Duplique_DixFois_UnSeulEffet));
        var (app, orch, _) = await CreateSutWithSimAsync(db);

        var charge = await orch.ChargeAsync(app, ChargeRequest(), "idem-dup");
        var payload = JsonSerializer.Serialize(new
        {
            reference = charge.ProviderReference,
            external_reference = charge.PaymentCode,
            status = "SUCCESSFUL",
            amount = 5000,
            currency = "XAF"
        });

        for (var i = 0; i < 10; i++)
        {
            var http = CreateHttpRequest(payload);
            var (status, _) = await orch.HandleWebhookAsync("campay", http.Request);
            Assert.Equal(StatusCodes.Status200OK, status);
        }

        Assert.Equal(1, await db.MobileMoneyWebhookEvents.CountAsync());
        Assert.Equal(1, await db.PaymentTransactions.CountAsync(x => x.Status == PaymentStatus.Succeeded));
    }

    [Fact]
    public async Task Webhook_MontantDivergent_RequiresReview()
    {
        await using var db = TestDbContextFactory.Create(nameof(Webhook_MontantDivergent_RequiresReview));
        var (app, orch, _) = await CreateSutWithSimAsync(db);

        var charge = await orch.ChargeAsync(app, ChargeRequest(), "idem-amt");
        var payload = JsonSerializer.Serialize(new
        {
            reference = charge.ProviderReference,
            external_reference = charge.PaymentCode,
            status = "SUCCESSFUL",
            amount = 99999,
            currency = "XAF"
        });

        await orch.HandleWebhookAsync("campay", CreateHttpRequest(payload).Request);

        var payment = await db.PaymentTransactions.SingleAsync();
        Assert.Equal(PaymentStatus.RequiresReview, payment.Status);
        Assert.Equal("AMOUNT_MISMATCH", payment.FailureCode);
    }

    [Fact]
    public async Task Webhook_SignatureInvalide_Rejetee()
    {
        await using var db = TestDbContextFactory.Create(nameof(Webhook_SignatureInvalide_Rejetee));
        var options = MsOptions.Create(new MobileMoneyOptions
        {
            Currency = "XAF",
            Providers = new MobileMoneyProvidersOptions
            {
                CamPay = new CamPayProviderOptions { Enabled = true, Environment = "Sandbox" }
            }
        });
        var secrets = MsOptions.Create(new CamPaySecretsOptions
        {
            Username = "user",
            Password = "pass",
            WebhookSecret = "real-secret-not-placeholder"
        });
        var campay = new CamPayMobileMoneyGateway(
            Mock.Of<IHttpClientFactory>(),
            options,
            secrets,
            NullLogger<CamPayMobileMoneyGateway>.Instance);

        var payload = """{"status":"SUCCESSFUL"}""";
        var http = CreateHttpRequest(payload);
        http.Request.Headers["X-CamPay-Signature"] = "deadbeef";

        await Assert.ThrowsAsync<InvalidOperationException>(() => campay.ParseWebhookAsync(http.Request));
    }

    [Fact]
    public async Task OrangeDirect_NotSupported()
    {
        var gw = new OrangeMoneyDirectGateway();
        var result = await gw.InitiateAsync(new MobileMoneyPaymentRequest(
            "PAY-1", "ORANGE", "237690000000", 1000, "XAF", "test", null));
        Assert.True(result.NotSupported);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MtnDirect_NotSupported()
    {
        var gw = new MtnMomoDirectGateway();
        var result = await gw.InitiateAsync(new MobileMoneyPaymentRequest(
            "PAY-1", "MTN", "237670000000", 1000, "XAF", "test", null));
        Assert.True(result.NotSupported);
    }

    [Fact]
    public void StateMachine_Succeeded_NeRepassePasAFailed()
    {
        Assert.False(MobileMoneyStateMachine.CanTransition(PaymentStatus.Succeeded, PaymentStatus.Failed));
        Assert.False(MobileMoneyStateMachine.CanTransition(PaymentStatus.Succeeded, PaymentStatus.Pending));
        Assert.True(MobileMoneyStateMachine.CanTransition(PaymentStatus.Succeeded, PaymentStatus.RefundPending));
    }

    [Theory]
    [InlineData("+237690123456", true)]
    [InlineData("237690123456", true)]
    [InlineData("690123456", true)]
    [InlineData("+237590123456", false)]
    [InlineData("123", false)]
    public void PhoneValidator_Cameroon(string input, bool expected)
    {
        var ok = MobileMoneyPhoneValidator.TryNormalizeCameroonPhone(input, out var normalized, out var masked);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.StartsWith("2376", normalized);
            Assert.Contains("**", masked);
        }
    }

    private static MobileMoneyChargeRequest ChargeRequest(
        string channel = "ORANGE",
        string phone = "+237690000000") =>
        new("CUST-1", "parent@test.com", "Parent Test", null, "MM-OFFER", "MONTHLY", channel, phone);

    private static async Task<(ClientApplication App, MobileMoneyOrchestrator Orch)> CreateSutAsync(ApplicationDbContext db)
    {
        var (app, orch, _) = await CreateSutWithSimAsync(db);
        return (app, orch);
    }

    private static async Task<(ClientApplication App, MobileMoneyOrchestrator Orch, LocalSimulatedMobileMoneyGateway Sim)> CreateSutWithSimAsync(
        ApplicationDbContext db)
    {
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db, "TUTORSPHERE");
        var product = new Product
        {
            ClientApplicationId = app.Id,
            ProductCode = "MM-OFFER",
            Name = "Offre MM",
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.PricingPlans.Add(new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = "MONTHLY",
            Name = "Mensuel XAF",
            Currency = "xaf",
            Amount = 5000m,
            BillingInterval = BillingInterval.Monthly,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var options = MsOptions.Create(new MobileMoneyOptions
        {
            Currency = "XAF",
            ChargeExpiryMinutes = 15,
            Providers = new MobileMoneyProvidersOptions
            {
                CamPay = new CamPayProviderOptions { Enabled = true, Environment = "Local" }
            }
        });

        var sim = new LocalSimulatedMobileMoneyGateway();
        var gateways = new IMobileMoneyGateway[]
        {
            new CamPayMobileMoneyGateway(
                Mock.Of<IHttpClientFactory>(),
                options,
                MsOptions.Create(new CamPaySecretsOptions()),
                NullLogger<CamPayMobileMoneyGateway>.Instance),
            new OrangeMoneyDirectGateway(),
            new MtnMomoDirectGateway()
        };

        var orch = new MobileMoneyOrchestrator(
            db,
            gateways,
            sim,
            options,
            Mock.Of<IAuditService>(),
            NullLogger<MobileMoneyOrchestrator>.Instance);

        return (app, orch, sim);
    }

    private static DefaultHttpContext CreateHttpRequest(string payload)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        return context;
    }
}
