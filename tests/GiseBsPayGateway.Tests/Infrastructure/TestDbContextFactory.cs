using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Tests.Infrastructure;

public static class TestDbContextFactory
{
    public static ApplicationDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public static async Task<(ClientApplication App, string RawApiKey, ApplicationApiKey ApiKey)> SeedAppWithApiKeyAsync(
        ApplicationDbContext db,
        string appCode = "BOUTIQUEGISE")
    {
        var apiKeyService = new ApiKeyService();
        var (rawKey, prefix, hash) = apiKeyService.GenerateApiKey();

        var app = new ClientApplication
        {
            AppCode = appCode,
            Name = $"Test {appCode}",
            IsActive = true
        };
        db.ClientApplications.Add(app);
        await db.SaveChangesAsync();

        var apiKey = new ApplicationApiKey
        {
            ClientApplicationId = app.Id,
            Name = "Test key",
            KeyPrefix = prefix,
            KeyHash = hash,
            IsActive = true
        };
        db.ApplicationApiKeys.Add(apiKey);
        await db.SaveChangesAsync();

        return (app, rawKey, apiKey);
    }

    public static async Task<(Product Product, PricingPlan Plan)> SeedProductPlanAsync(
        ApplicationDbContext db,
        ClientApplication app,
        string productCode = "AGENT-CODE",
        string planCode = "MONTHLY",
        decimal amount = 24m)
    {
        var product = new Product
        {
            ClientApplicationId = app.Id,
            ProductCode = productCode,
            Name = productCode,
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var plan = new PricingPlan
        {
            ProductId = product.Id,
            PlanCode = planCode,
            Name = planCode,
            Currency = "USD",
            Amount = amount,
            BillingInterval = BillingInterval.Monthly,
            IsActive = true
        };
        db.PricingPlans.Add(plan);
        await db.SaveChangesAsync();

        return (product, plan);
    }
}
