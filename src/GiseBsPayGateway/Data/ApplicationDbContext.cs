using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Data;

public class ApplicationDbContext : IdentityDbContext<AdminUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<ClientApplication> ClientApplications => Set<ClientApplication>();
    public DbSet<ApplicationApiKey> ApplicationApiKeys => Set<ApplicationApiKey>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<StripeSettings> StripeSettings => Set<StripeSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ClientApplication>(e =>
        {
            e.HasIndex(x => x.AppCode).IsUnique();
            e.Property(x => x.AppCode).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        builder.Entity<ApplicationApiKey>(e =>
        {
            e.HasIndex(x => x.KeyPrefix);
            e.Property(x => x.KeyPrefix).HasMaxLength(12);
            e.Property(x => x.KeyHash).HasMaxLength(256);
            e.HasOne(x => x.ClientApplication).WithMany(x => x.ApiKeys).HasForeignKey(x => x.ClientApplicationId);
        });

        builder.Entity<Customer>(e =>
        {
            e.HasIndex(x => new { x.ClientApplicationId, x.CustomerCode }).IsUnique();
            e.Property(x => x.CustomerCode).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasOne(x => x.ClientApplication).WithMany(x => x.Customers).HasForeignKey(x => x.ClientApplicationId);
        });

        builder.Entity<Product>(e =>
        {
            e.HasIndex(x => new { x.ClientApplicationId, x.ProductCode }).IsUnique();
            e.Property(x => x.ProductCode).HasMaxLength(50);
            e.HasOne(x => x.ClientApplication).WithMany(x => x.Products).HasForeignKey(x => x.ClientApplicationId);
        });

        builder.Entity<PricingPlan>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.PlanCode }).IsUnique();
            e.Property(x => x.PlanCode).HasMaxLength(50);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Product).WithMany(x => x.PricingPlans).HasForeignKey(x => x.ProductId);
        });

        builder.Entity<PaymentTransaction>(e =>
        {
            e.HasIndex(x => x.PaymentCode).IsUnique();
            e.Property(x => x.PaymentCode).HasMaxLength(50);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.ClientApplication).WithMany(x => x.PaymentTransactions).HasForeignKey(x => x.ClientApplicationId);
            e.HasOne(x => x.Customer).WithMany(x => x.PaymentTransactions).HasForeignKey(x => x.CustomerId);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.PricingPlan).WithMany(x => x.PaymentTransactions).HasForeignKey(x => x.PricingPlanId);
            e.HasOne(x => x.Subscription).WithMany(x => x.PaymentTransactions).HasForeignKey(x => x.SubscriptionId);
        });

        builder.Entity<Subscription>(e =>
        {
            e.HasIndex(x => x.SubscriptionCode).IsUnique();
            e.Property(x => x.SubscriptionCode).HasMaxLength(50);
            e.HasOne(x => x.ClientApplication).WithMany(x => x.Subscriptions).HasForeignKey(x => x.ClientApplicationId);
            e.HasOne(x => x.Customer).WithMany(x => x.Subscriptions).HasForeignKey(x => x.CustomerId);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.PricingPlan).WithMany(x => x.Subscriptions).HasForeignKey(x => x.PricingPlanId);
        });

        builder.Entity<StripeWebhookEvent>(e =>
        {
            e.HasIndex(x => x.StripeEventId).IsUnique();
            e.Property(x => x.StripeEventId).HasMaxLength(100);
            e.Property(x => x.EventType).HasMaxLength(100);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(100);
        });

        builder.Entity<StripeSettings>(e =>
        {
            e.Property(x => x.PublishableKey).HasMaxLength(500);
            e.Property(x => x.SecretKey).HasMaxLength(500);
            e.Property(x => x.WebhookSecret).HasMaxLength(500);
        });
    }
}
