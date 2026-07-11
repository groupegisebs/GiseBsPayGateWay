using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GiseBsPayGateway.Tests.Services;

public class CurrencyConversionServiceTests
{
    [Theory]
    [InlineData(100, "cad", "cad", 100)]
    [InlineData(136, "cad", "usd", 100)] // 136 CAD / 1.36 = 100 USD
    [InlineData(100, "usd", "cad", 136)] // 100 USD * 1.36 = 136 CAD
    [InlineData(50, "cad", "usd", 36.76)] // 50 / 1.36 ≈ 36.76
    public async Task ConvertAsync_UtiliseRatesProvider(decimal amount, string from, string to, decimal expected)
    {
        var rates = new Mock<IExchangeRateProvider>();
        rates.Setup(r => r.GetRatesToCadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["cad"] = 1m,
                ["usd"] = 1.36m,
                ["eur"] = 1.48m,
                ["gbp"] = 1.72m,
                ["chf"] = 1.55m
            });

        var sut = new CurrencyConversionService(rates.Object);
        var result = await sut.ConvertAsync(amount, from, to);
        Assert.Equal(expected, result);
    }
}

public class BankOfCanadaExchangeRateProviderTests
{
    [Fact]
    public async Task GetRatesToCadAsync_ParseGroupeFxRatesDaily()
    {
        var json = """
            {
              "observations": [
                {
                  "d": "2019-12-31",
                  "FXVNDCAD": { "v": "0.000056" }
                },
                {
                  "d": "2026-07-10",
                  "FXUSDCAD": { "v": "1.4146" },
                  "FXEURCAD": { "v": "1.6161" },
                  "FXGBPCAD": { "v": "1.8970" },
                  "FXCHFCAD": { "v": "1.7516" },
                  "FXJPYCAD": { "v": "0.008750" },
                  "FXAUDCAD": { "v": "0.9836" },
                  "FXCNYCAD": { "v": "0.2087" }
                }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(json);
        var httpClient = new HttpClient(handler);
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new CurrencyConversionOptions
        {
            UseLiveRates = true,
            LiveRatesCacheMinutes = 60,
            BankOfCanadaUrl = "https://example.test/rates"
        });

        var sut = new BankOfCanadaExchangeRateProvider(
            httpClient,
            cache,
            options,
            Mock.Of<ILogger<BankOfCanadaExchangeRateProvider>>());

        var rates = await sut.GetRatesToCadAsync();

        Assert.Equal(1m, rates["cad"]);
        Assert.Equal(1.4146m, rates["usd"]);
        Assert.Equal(1.6161m, rates["eur"]);
        Assert.Equal(1.8970m, rates["gbp"]);
        Assert.Equal(1.7516m, rates["chf"]);
        Assert.Equal(0.008750m, rates["jpy"]);
        Assert.Equal(0.9836m, rates["aud"]);
        Assert.Equal(0.2087m, rates["cny"]);
        Assert.Equal(0.000056m, rates["vnd"]);
    }

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

public class PricingPlanCurrencyVariantServiceTests
{
    [Fact]
    public async Task ResolveForCheckout_SansDeviseVerrouillee_RetournePlanExistant()
    {
        await using var db = TestDbContextFactory.Create(nameof(ResolveForCheckout_SansDeviseVerrouillee_RetournePlanExistant));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await SeedCadPlanAsync(db, app);

        var sut = CreateSut(db, Mock.Of<IStripeService>());
        var result = await sut.ResolveForCheckoutAsync(product, "MONTHLY", lockedCurrency: null);

        Assert.Equal(plan.Id, result.Id);
        Assert.Equal("cad", result.Currency);
    }

    [Fact]
    public async Task ResolveForCheckout_DeviseVerrouilleeExistante_RetournePlanMatching()
    {
        await using var db = TestDbContextFactory.Create(nameof(ResolveForCheckout_DeviseVerrouilleeExistante_RetournePlanMatching));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, _) = await SeedCadPlanAsync(db, app);
        var usdPlan = new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = "MONTHLY",
            Name = "Mensuel USD",
            Amount = 36.76m,
            Currency = "usd",
            BillingInterval = BillingInterval.Monthly,
            IsActive = true
        };
        db.PricingPlans.Add(usdPlan);
        await db.SaveChangesAsync();
        product.PricingPlans.Add(usdPlan);

        var sut = CreateSut(db, Mock.Of<IStripeService>());
        var result = await sut.ResolveForCheckoutAsync(product, "MONTHLY", "usd");

        Assert.Equal(usdPlan.Id, result.Id);
    }

    [Fact]
    public async Task ResolveForCheckout_DeviseAbsente_CreeVarianteEtPriceStripe()
    {
        await using var db = TestDbContextFactory.Create(nameof(ResolveForCheckout_DeviseAbsente_CreeVarianteEtPriceStripe));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, cadPlan) = await SeedCadPlanAsync(db, app, amount: 50m);

        var stripe = new Mock<IStripeService>();
        stripe.Setup(s => s.EnsureStripeProductAsync(product, It.IsAny<CancellationToken>()))
            .ReturnsAsync("prod_test");
        stripe.Setup(s => s.EnsureStripePriceAsync(It.IsAny<PricingPlan>(), "prod_test", It.IsAny<CancellationToken>()))
            .ReturnsAsync("price_usd_auto");

        var sut = CreateSut(db, stripe.Object);
        var result = await sut.ResolveForCheckoutAsync(product, "MONTHLY", "usd");

        Assert.NotEqual(cadPlan.Id, result.Id);
        Assert.Equal("usd", result.Currency);
        Assert.Equal(36.76m, result.Amount); // 50 CAD / 1.36
        Assert.Equal("price_usd_auto", result.StripePriceId);
        Assert.Equal(2, db.PricingPlans.Count(x => x.ProductId == product.Id && x.IsActive));
        stripe.Verify(s => s.EnsureStripePriceAsync(
            It.Is<PricingPlan>(p => p.Currency == "usd" && p.Amount == 36.76m),
            "prod_test",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<(Product Product, PricingPlan Plan)> SeedCadPlanAsync(
        Data.ApplicationDbContext db,
        ClientApplication app,
        decimal amount = 50m)
    {
        var product = new Product
        {
            ClientApplicationId = app.Id,
            ProductCode = "COMPTA-DOC",
            Name = "Compta doc",
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var plan = new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = "MONTHLY",
            Name = "Mensuel",
            Amount = amount,
            Currency = "cad",
            BillingInterval = BillingInterval.Monthly,
            IsActive = true
        };
        db.PricingPlans.Add(plan);
        await db.SaveChangesAsync();
        product.PricingPlans.Add(plan);
        return (product, plan);
    }

    private static PricingPlanCurrencyVariantService CreateSut(
        Data.ApplicationDbContext db,
        IStripeService stripe)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CurrencyConversionOptions());
        var rates = new Mock<IExchangeRateProvider>();
        rates.Setup(r => r.GetRatesToCadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["cad"] = 1m,
                ["usd"] = 1.36m,
                ["eur"] = 1.48m,
                ["gbp"] = 1.72m,
                ["chf"] = 1.55m
            });

        return new PricingPlanCurrencyVariantService(
            db,
            new CurrencyConversionService(rates.Object),
            stripe,
            Mock.Of<IAuditService>(),
            options,
            Mock.Of<ILogger<PricingPlanCurrencyVariantService>>());
    }
}
