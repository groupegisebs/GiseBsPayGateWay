using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace GiseBsPayGateway.Tests.Infrastructure;

public class PayGatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Testing:InMemoryDatabaseName"] = _dbName,
                ["Jwt:SecretKey"] = "integration-test-secret-key-32-chars!",
                ["Jwt:Issuer"] = "GiseBsPayGateway-Test",
                ["Jwt:Audience"] = "GiseBsPayGatewayClients-Test",
                ["Seed:AdminEmail"] = "admin@test.local",
                ["Seed:AdminPassword"] = "Admin123!",
                ["Seed:TestEmail"] = "test@test.local",
                ["Seed:TestPassword"] = "Test123!"
            });
        });

        builder.ConfigureServices(services =>
        {
            var stripe = new Mock<IStripeService>();
            stripe.Setup(s => s.CreateCheckoutSessionAsync(
                    It.IsAny<Entities.PaymentTransaction>(), It.IsAny<Entities.Customer>(), It.IsAny<Entities.PricingPlan>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>(),
                    It.IsAny<BillingAddressDto?>(), It.IsAny<CustomerUpdateDto?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(("cs_test_integration", "https://checkout.stripe.com/test", "cs_secret_integration"));
            stripe.Setup(s => s.GetOrCreateStripeCustomerAsync(It.IsAny<Entities.Customer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
            stripe.Setup(s => s.GetCustomerLockedCurrencyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            services.RemoveAll<IStripeService>();
            services.AddSingleton(stripe.Object);

            var settings = new Mock<IStripeSettingsProvider>();
            settings.Setup(s => s.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StripeSettingsSnapshot("pk_test_integration", "sk_test", "whsec_test", false, false, "DEV"));

            services.RemoveAll<IStripeSettingsProvider>();
            services.AddSingleton(settings.Object);
        });
    }
}
