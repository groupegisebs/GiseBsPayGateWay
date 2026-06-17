using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;
using Stripe.Checkout;
using ProductEntity = GiseBsPayGateway.Entities.Product;
using CustomerEntity = GiseBsPayGateway.Entities.Customer;

namespace GiseBsPayGateway.Tests.Services;

public class CollectedTaxServiceTests
{
    [Fact]
    public async Task SaveFromCheckoutCompletedAsync_SessionPaidAndIntentSucceeded_PersistsTaxRecord()
    {
        await using var db = TestDbContextFactory.Create(nameof(SaveFromCheckoutCompletedAsync_SessionPaidAndIntentSucceeded_PersistsTaxRecord));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var payment = await SeedSucceededPaymentAsync(db, app, product, plan);

        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        stripeDetails.Setup(s => s.GetPaymentIntentStatusAsync("pi_paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync("succeeded");
        stripeDetails.Setup(s => s.GetStripeTaxTransactionIdAsync("pi_paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var session = CreatePaidSession(payment.PaymentCode);
        var sut = CreateService(db, stripeDetails);

        await sut.SaveFromCheckoutCompletedAsync(payment, session);

        Assert.Single(db.CollectedTaxRecords);
        Assert.Equal(payment.PaymentCode, db.CollectedTaxRecords.Single().PaymentCode);
    }

    [Fact]
    public async Task SaveFromCheckoutCompletedAsync_SessionUnpaid_DoesNotPersistTaxRecord()
    {
        await using var db = TestDbContextFactory.Create(nameof(SaveFromCheckoutCompletedAsync_SessionUnpaid_DoesNotPersistTaxRecord));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var payment = await SeedSucceededPaymentAsync(db, app, product, plan);

        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        var session = CreatePaidSession(payment.PaymentCode);
        session.PaymentStatus = "unpaid";

        var sut = CreateService(db, stripeDetails);

        await sut.SaveFromCheckoutCompletedAsync(payment, session);

        Assert.Empty(db.CollectedTaxRecords);
        stripeDetails.Verify(
            s => s.GetPaymentIntentStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveFromCheckoutCompletedAsync_PaymentIntentNotSucceeded_DoesNotPersistTaxRecord()
    {
        await using var db = TestDbContextFactory.Create(nameof(SaveFromCheckoutCompletedAsync_PaymentIntentNotSucceeded_DoesNotPersistTaxRecord));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var payment = await SeedSucceededPaymentAsync(db, app, product, plan);

        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        stripeDetails.Setup(s => s.GetPaymentIntentStatusAsync("pi_paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync("requires_payment_method");

        var session = CreatePaidSession(payment.PaymentCode);
        var sut = CreateService(db, stripeDetails);

        await sut.SaveFromCheckoutCompletedAsync(payment, session);

        Assert.Empty(db.CollectedTaxRecords);
    }

    [Fact]
    public async Task SaveFromCheckoutCompletedAsync_PaymentNotSucceeded_DoesNotPersistTaxRecord()
    {
        await using var db = TestDbContextFactory.Create(nameof(SaveFromCheckoutCompletedAsync_PaymentNotSucceeded_DoesNotPersistTaxRecord));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var payment = await SeedPendingPaymentAsync(db, app, product, plan);

        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        stripeDetails.Setup(s => s.GetPaymentIntentStatusAsync("pi_paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync("succeeded");

        var session = CreatePaidSession(payment.PaymentCode);
        var sut = CreateService(db, stripeDetails);

        await sut.SaveFromCheckoutCompletedAsync(payment, session);

        Assert.Empty(db.CollectedTaxRecords);
    }

    [Fact]
    public async Task SaveFromStripeInvoiceAsync_InvoiceNotPaid_DoesNotPersistTaxRecord()
    {
        await using var db = TestDbContextFactory.Create(nameof(SaveFromStripeInvoiceAsync_InvoiceNotPaid_DoesNotPersistTaxRecord));
        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        var sut = CreateService(db, stripeDetails);

        var invoice = new Invoice
        {
            Id = "in_open",
            Status = "open",
            Currency = "cad",
            Subtotal = 10000,
            Total = 11498
        };

        await sut.SaveFromStripeInvoiceAsync(invoice, null);

        Assert.Empty(db.CollectedTaxRecords);
    }

    [Fact]
    public async Task RemoveForFailedPaymentAsync_RemovesExistingRecord()
    {
        await using var db = TestDbContextFactory.Create(nameof(RemoveForFailedPaymentAsync_RemovesExistingRecord));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);
        var payment = await SeedSucceededPaymentAsync(db, app, product, plan);

        var record = new CollectedTaxRecord
        {
            ClientApplicationId = app.Id,
            PaymentTransactionId = payment.Id,
            PaymentCode = payment.PaymentCode,
            TransactionReference = "pi_paid",
            CollectedAt = DateTime.UtcNow,
            Currency = "cad",
            AmountSubtotal = 100m,
            TaxAmountTotal = 14.98m,
            GrossAmount = 114.98m,
            Lines =
            [
                new CollectedTaxLine
                {
                    SortOrder = 0,
                    Code = "ca_gst",
                    Name = "GST",
                    Rate = 0.05m,
                    Amount = 5m,
                    Type = "federal"
                }
            ]
        };
        db.CollectedTaxRecords.Add(record);
        await db.SaveChangesAsync();

        var sut = CreateService(db, new Mock<IStripePaymentDetailsService>());

        await sut.RemoveForFailedPaymentAsync(payment.Id, payment.PaymentCode, "pi_paid");

        Assert.Empty(db.CollectedTaxRecords);
        Assert.Empty(db.CollectedTaxLines);
    }

    private static CollectedTaxService CreateService(
        GiseBsPayGateway.Data.ApplicationDbContext db,
        Mock<IStripePaymentDetailsService> stripeDetails) =>
        new(db, stripeDetails.Object, NullLogger<CollectedTaxService>.Instance);

    private static Session CreatePaidSession(string paymentCode) =>
        new()
        {
            Id = "cs_test",
            PaymentStatus = "paid",
            PaymentIntentId = "pi_paid",
            AmountSubtotal = 10000,
            AmountTotal = 11498,
            Metadata = new Dictionary<string, string> { ["payment_code"] = paymentCode },
            TotalDetails = new SessionTotalDetails
            {
                AmountTax = 1498,
                Breakdown = new SessionTotalDetailsBreakdown
                {
                    Taxes =
                    [
                        new SessionTotalDetailsBreakdownTax
                        {
                            Amount = 1498,
                            TaxableAmount = 10000,
                            Rate = new TaxRate { DisplayName = "GST", Percentage = 14.98m, TaxType = "gst", Country = "CA" }
                        }
                    ]
                }
            },
            CustomerDetails = new SessionCustomerDetails
            {
                Address = new Address { Country = "CA", State = "QC" }
            }
        };

    private static async Task<PaymentTransaction> SeedSucceededPaymentAsync(
        GiseBsPayGateway.Data.ApplicationDbContext db,
        ClientApplication app,
        ProductEntity product,
        PricingPlan plan)
    {
        var customer = new CustomerEntity
        {
            ClientApplicationId = app.Id,
            CustomerCode = "CUST-1",
            Email = "test@example.com"
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = "PAY-TEST-001",
            Status = PaymentStatus.Succeeded,
            Amount = 100m,
            Currency = "CAD",
            TaxAmount = 14.98m,
            PaidAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private static async Task<PaymentTransaction> SeedPendingPaymentAsync(
        GiseBsPayGateway.Data.ApplicationDbContext db,
        ClientApplication app,
        ProductEntity product,
        PricingPlan plan)
    {
        var customer = new CustomerEntity
        {
            ClientApplicationId = app.Id,
            CustomerCode = "CUST-2",
            Email = "pending@example.com"
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = "PAY-TEST-002",
            Status = PaymentStatus.Pending,
            Amount = 100m,
            Currency = "CAD"
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }
}
