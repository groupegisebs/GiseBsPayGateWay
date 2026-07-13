using System.Text;
using AspNetCoreRateLimit;
using GiseBsPayGateway.Configuration;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Middleware;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUbuntu1Overrides();
builder.Configuration.AddServerSecretsFile();
builder.ApplyListenUrl();

builder.Services.Configure<DeploymentSettings>(
    builder.Configuration.GetSection(DeploymentSettings.SectionName));

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/gisebs-pay-gateway-.log", rollingInterval: RollingInterval.Day);
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<StripeSecretsOptions>(builder.Configuration.GetSection(StripeSecretsOptions.SectionName));
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));

if (builder.Environment.IsEnvironment("Testing"))
{
    var testDbName = builder.Configuration["Testing:InMemoryDatabaseName"] ?? Guid.NewGuid().ToString();
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase(testDbName));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration[$"{DeploymentSettings.SectionName}:ConnectionString"]
        ?? throw new InvalidOperationException(
            "Connection string introuvable. Définissez UBUNTU1_CONNECTION_STRING ou ConnectionStrings:DefaultConnection.");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
}

builder.Services.AddIdentity<AdminUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey))
        };
    });

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IStripeEnvironmentAccessor, HttpStripeEnvironmentAccessor>();
builder.Services.AddScoped<IStripeSettingsProvider, StripeSettingsProvider>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IStripePaymentDetailsService, StripePaymentDetailsService>();
builder.Services.Configure<GiseBsPayGateway.Options.CurrencyConversionOptions>(
    builder.Configuration.GetSection(GiseBsPayGateway.Options.CurrencyConversionOptions.SectionName));
builder.Services.AddHttpClient<IExchangeRateProvider, BankOfCanadaExchangeRateProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GiseBsPayGateway/1.0");
});
builder.Services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();
builder.Services.AddScoped<IPricingPlanCurrencyVariantService, PricingPlanCurrencyVariantService>();
builder.Services.AddScoped<ISubscriptionSyncService, SubscriptionSyncService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IGisebsInvoiceCodeGenerator, GisebsInvoiceCodeGenerator>();
builder.Services.AddSingleton<IInvoicePdfGenerator, InvoicePdfGenerator>();
builder.Services.AddSingleton<IInvoiceFileStorage, InvoiceFileStorage>();
builder.Services.AddSingleton<IInvoiceLinkBuilder, InvoiceLinkBuilder>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IConnectService, ConnectService>();
builder.Services.AddScoped<ITransferService, ConnectTransferService>();
builder.Services.Configure<GiseBsPayGateway.Options.PayoutCallbackOptions>(
    builder.Configuration.GetSection(GiseBsPayGateway.Options.PayoutCallbackOptions.SectionName));
builder.Services.AddHttpClient("PayoutCallback", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<IPayoutCallbackNotifier, PayoutCallbackNotifier>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<ICollectedTaxService, CollectedTaxService>();

builder.Services.AddHealthChecks();
builder.Services.AddControllers();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "logs"));

try
{
    await DbSeeder.SeedAsync(app.Services);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Échec du seed / migrations au démarrage");
    throw;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            if (ex is not null)
            {
                Log.Error(ex, "Erreur non gérée {Path}", context.Request.Path);
            }

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = ex is InvalidOperationException
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                    ex?.Message ?? "Erreur interne Pay Gateway.",
                    null));
                return;
            }

            context.Response.Redirect("/Error");
        });
    });
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseIpRateLimiting();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiKeyAuthentication();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapRazorPages();

app.Run();

public partial class Program;
