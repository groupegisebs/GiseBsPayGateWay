using GiseBsPayGateway.Data;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        await db.Database.MigrateAsync();

        await EnsureAdminUserAsync(userManager, auditService, seedOptions.AdminEmail, seedOptions.AdminPassword, "Administrateur GISEBS");
        await EnsureAdminUserAsync(userManager, auditService, seedOptions.TestEmail, seedOptions.TestPassword, "Compte test GISEBS");

        if (!await db.ClientApplications.AnyAsync())
        {
            var holoTuto = new ClientApplication
            {
                AppCode = "HOLOTUTO",
                Name = "HoloTuto",
                Description = "Application de tutoriels holographiques",
                AllowedDomains = "localhost,holotuto.com",
                IsActive = true
            };
            var cogniDoc = new ClientApplication
            {
                AppCode = "COGNIDOC",
                Name = "CogniDoc",
                Description = "Gestion documentaire intelligente",
                AllowedDomains = "localhost,cognidoc.com",
                IsActive = true
            };
            var warrantySafe = new ClientApplication
            {
                AppCode = "WARRANTYSAFE",
                Name = "WarrantySafe",
                Description = "Gestion des garanties produits",
                AllowedDomains = "localhost,warrantysafe.com",
                IsActive = true
            };

            db.ClientApplications.AddRange(holoTuto, cogniDoc, warrantySafe);
            await db.SaveChangesAsync();

            foreach (var app in new[] { holoTuto, cogniDoc, warrantySafe })
            {
                var (rawKey, prefix, hash) = apiKeyService.GenerateApiKey();
                db.ApplicationApiKeys.Add(new ApplicationApiKey
                {
                    ClientApplicationId = app.Id,
                    Name = "Clé par défaut",
                    KeyPrefix = prefix,
                    KeyHash = hash,
                    IsActive = true
                });
                await auditService.LogAsync("DefaultApiKeyCreated", nameof(ApplicationApiKey), app.Id.ToString(), true,
                    $"App={app.AppCode}; Prefix={prefix}", app.AppCode);
                logger.LogWarning("Clé API seed pour {AppCode} — prefix={Prefix} — CONSULTEZ LES LOGS DE DÉMARRAGE UNE SEULE FOIS: {RawKey}",
                    app.AppCode, prefix, rawKey);
            }

            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureAdminUserAsync(
        UserManager<AdminUser> userManager,
        IAuditService auditService,
        string email,
        string password,
        string fullName)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new AdminUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await auditService.LogAsync("AdminUserCreated", nameof(AdminUser), user.Id, true, email);
        }
    }
}
