using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Services;
using GiseBsPayGateway.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
using Stripe.Checkout;
using InvoiceServiceImpl = GiseBsPayGateway.Services.InvoiceService;

namespace GiseBsPayGateway.Tests.Services;

public class StripeCheckoutFinancialsTests
{
    [Fact]
    public void ApplySessionTaxToPayment_StoresTaxSubtotalAndGross()
    {
        var payment = new PaymentTransaction { Amount = 100m };
        var session = new Session
        {
            AmountSubtotal = 10000,
            AmountTotal = 11300,
            TotalDetails = new SessionTotalDetails { AmountTax = 1300 },
            CustomerDetails = new SessionCustomerDetails
            {
                Address = new Address { Country = "CA", State = "QC" }
            }
        };

        StripeCheckoutFinancials.ApplySessionTaxToPayment(payment, session);

        Assert.Equal(100m, payment.AmountSubtotal);
        Assert.Equal(13m, payment.TaxAmount);
        Assert.Equal(113m, payment.GrossAmount);
        Assert.Equal("CA", payment.BillingCountry);
        Assert.Equal("QC", payment.BillingState);
    }

    [Fact]
    public void ApplySessionTaxToPayment_DerivesTaxFromTotalMinusSubtotal()
    {
        var payment = new PaymentTransaction { Amount = 100m };
        var session = new Session
        {
            AmountSubtotal = 10000,
            AmountTotal = 11300
        };

        StripeCheckoutFinancials.ApplySessionTaxToPayment(payment, session);

        Assert.Equal(100m, payment.AmountSubtotal);
        Assert.Equal(13m, payment.TaxAmount);
        Assert.Equal(113m, payment.GrossAmount);
    }

    [Fact]
    public void NeedsTaxAmount_TrueWhenNullOrZero()
    {
        Assert.True(StripeCheckoutFinancials.NeedsTaxAmount(new PaymentTransaction()));
        Assert.True(StripeCheckoutFinancials.NeedsTaxAmount(new PaymentTransaction { TaxAmount = 0m }));
        Assert.False(StripeCheckoutFinancials.NeedsTaxAmount(new PaymentTransaction { TaxAmount = 14.98m }));
    }

    [Fact]
    public void ApplyCollectedTaxRecordToPayment_SyncsTaxSubtotalAndGross()
    {
        var payment = new PaymentTransaction { Amount = 100m };
        var record = new CollectedTaxRecord
        {
            AmountSubtotal = 100m,
            TaxAmountTotal = 14.98m,
            GrossAmount = 114.98m,
            BillingCountry = "CA",
            BillingState = "QC"
        };

        StripeCheckoutFinancials.ApplyCollectedTaxRecordToPayment(payment, record);

        Assert.Equal(100m, payment.AmountSubtotal);
        Assert.Equal(14.98m, payment.TaxAmount);
        Assert.Equal(114.98m, payment.GrossAmount);
        Assert.Equal("CA", payment.BillingCountry);
        Assert.Equal("QC", payment.BillingState);
    }

    [Fact]
    public void ApplyStripeInvoiceTaxToPayment_OverwritesMissingTaxFromTotal()
    {
        var payment = new PaymentTransaction { Amount = 5m };
        var invoice = new Invoice
        {
            TotalExcludingTax = 500,
            Total = 565,
            CustomerAddress = new Address { Country = "CA", State = "ON" }
        };

        StripeCheckoutFinancials.ApplyStripeInvoiceTaxToPayment(payment, invoice);

        Assert.Equal(5m, payment.AmountSubtotal);
        Assert.Equal(0.65m, payment.TaxAmount);
        Assert.Equal(5.65m, payment.GrossAmount);
        Assert.Equal("CA", payment.BillingCountry);
        Assert.Equal("ON", payment.BillingState);
    }

    [Fact]
    public void ApplyBalanceTransactionToPayment_StoresFeeNetAndBalanceTransactionId()
    {
        var payment = new PaymentTransaction { Amount = 100m };
        var details = new StripeBalanceTransactionDetails(2.90m, 97.10m, 100m, "txn_123");

        StripeCheckoutFinancials.ApplyBalanceTransactionToPayment(payment, details);

        Assert.Equal(2.90m, payment.StripeFee);
        Assert.Equal(97.10m, payment.NetAmount);
        Assert.Equal("txn_123", payment.StripeBalanceTransactionId);
        Assert.Null(payment.GrossAmount);
    }

    [Fact]
    public void ResolveCustomerTotal_PrefersGrossOverCatalogAmount()
    {
        var payment = new PaymentTransaction
        {
            Amount = 100m,
            GrossAmount = 113m,
            AmountSubtotal = 100m,
            TaxAmount = 13m
        };

        Assert.Equal(113m, StripeCheckoutFinancials.ResolveCustomerTotal(payment));
    }
}

public class InvoiceServiceFinancialsTests
{
    [Fact]
    public async Task EnsureInvoiceForPaymentAsync_BackfillsStripeFeeWhenMissing()
    {
        await using var db = TestDbContextFactory.Create(nameof(EnsureInvoiceForPaymentAsync_BackfillsStripeFeeWhenMissing));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(db, app);

        var customer = new Entities.Customer
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
            PaymentCode = "PAY-TEST-1",
            Status = PaymentStatus.Succeeded,
            Amount = 100m,
            Currency = "cad",
            StripePaymentIntentId = "pi_test_1",
            PaidAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();

        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        stripeDetails.Setup(s => s.GetBalanceTransactionDetailsAsync("pi_test_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeBalanceTransactionDetails(2.90m, 97.10m, 100m, "txn_test_1"));

        var sut = new InvoiceServiceImpl(
            db,
            new GisebsInvoiceCodeGenerator(db),
            new InvoicePdfGenerator(),
            new InvoiceFileStorage(new Microsoft.Extensions.Options.OptionsWrapper<GiseBsPayGateway.Configuration.DeploymentSettings>(
                new GiseBsPayGateway.Configuration.DeploymentSettings { AppRoot = Path.GetTempPath() })),
            stripeDetails.Object,
            Mock.Of<ILogger<InvoiceServiceImpl>>());

        var invoice = await sut.EnsureInvoiceForPaymentAsync(payment, CancellationToken.None);

        Assert.NotNull(invoice);
        Assert.Equal(2.90m, payment.StripeFee);
        Assert.Equal(97.10m, payment.NetAmount);
        Assert.Equal("txn_test_1", payment.StripeBalanceTransactionId);
        Assert.Equal(2.90m, invoice!.StripeFee);
        Assert.Equal(97.10m, invoice.NetAmount);
    }

    [Fact]
    public async Task EnrichSuccessfulPaymentFinancialsAsync_SubscriptionInvoice_BackfillsTaxAmount()
    {
        await using var db = TestDbContextFactory.Create(nameof(EnrichSuccessfulPaymentFinancialsAsync_SubscriptionInvoice_BackfillsTaxAmount));
        var (app, _, _) = await TestDbContextFactory.SeedAppWithApiKeyAsync(db);
        var (product, plan) = await TestDbContextFactory.SeedProductPlanAsync(
            db, app, "HOLOTUTO", "MONTHLY", 100m);

        var customer = new Entities.Customer
        {
            ClientApplicationId = app.Id,
            CustomerCode = "CUST-1",
            Email = "test@example.com"
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var subscription = new Entities.Subscription
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            SubscriptionCode = "SUB-TEST-1",
            StripeSubscriptionId = "sub_test_1",
            Status = SubscriptionStatus.Active
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            SubscriptionId = subscription.Id,
            PaymentCode = "PAY-TEST-SUB",
            Status = PaymentStatus.Succeeded,
            Amount = 100m,
            Currency = "usd",
            StripeCheckoutSessionId = "cs_test_sub",
            StripePaymentIntentId = "pi_test_sub",
            PaidAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();

        var stripeInvoice = new Invoice
        {
            Id = "in_test_sub",
            TotalExcludingTax = 10000,
            Total = 11498,
            CustomerAddress = new Address { Country = "CA", State = "QC" }
        };

        var stripeDetails = new Mock<IStripePaymentDetailsService>();
        stripeDetails.Setup(s => s.GetCheckoutSessionAsync("cs_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session
            {
                Id = "cs_test_sub",
                PaymentStatus = "paid",
                SubscriptionId = "sub_test_1",
                AmountSubtotal = 10000,
                AmountTotal = 10000
            });
        stripeDetails.Setup(s => s.GetSubscriptionAsync("sub_test_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stripe.Subscription
            {
                Id = "sub_test_1",
                LatestInvoiceId = "in_test_sub"
            });
        stripeDetails.Setup(s => s.GetInvoiceAsync("in_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeInvoice);
        stripeDetails.Setup(s => s.GetBalanceTransactionDetailsAsync("pi_test_sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeBalanceTransactionDetails(3.20m, 111.78m, 114.98m, "txn_test_sub"));

        var sut = new InvoiceServiceImpl(
            db,
            new GisebsInvoiceCodeGenerator(db),
            new InvoicePdfGenerator(),
            new InvoiceFileStorage(new Microsoft.Extensions.Options.OptionsWrapper<GiseBsPayGateway.Configuration.DeploymentSettings>(
                new GiseBsPayGateway.Configuration.DeploymentSettings { AppRoot = Path.GetTempPath() })),
            stripeDetails.Object,
            Mock.Of<ILogger<InvoiceServiceImpl>>());

        await sut.EnrichSuccessfulPaymentFinancialsAsync(payment, cancellationToken: CancellationToken.None);

        Assert.Equal(100m, payment.AmountSubtotal);
        Assert.Equal(14.98m, payment.TaxAmount);
        Assert.Equal(114.98m, payment.GrossAmount);
        Assert.Equal("CA", payment.BillingCountry);
    }
}
