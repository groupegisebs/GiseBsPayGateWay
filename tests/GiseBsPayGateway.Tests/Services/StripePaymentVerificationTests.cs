using GiseBsPayGateway.Services;
using Stripe;
using Stripe.Checkout;

namespace GiseBsPayGateway.Tests.Services;

public class StripePaymentVerificationTests
{
    [Theory]
    [InlineData("paid", true)]
    [InlineData("PAID", true)]
    [InlineData("unpaid", false)]
    [InlineData("no_payment_required", false)]
    public void IsCheckoutSessionPaymentConfirmed_RespectsPaymentStatus(string paymentStatus, bool expected)
    {
        var session = new Session { PaymentStatus = paymentStatus };

        Assert.Equal(expected, StripePaymentVerification.IsCheckoutSessionPaymentConfirmed(session));
    }

    [Theory]
    [InlineData("succeeded", true)]
    [InlineData("Succeeded", true)]
    [InlineData("requires_payment_method", false)]
    [InlineData("processing", false)]
    public void IsPaymentIntentSucceeded_RespectsStatus(string status, bool expected)
    {
        Assert.Equal(expected, StripePaymentVerification.IsPaymentIntentSucceeded(status));
    }

    [Theory]
    [InlineData("paid", true)]
    [InlineData("open", false)]
    [InlineData("void", false)]
    public void IsInvoicePaid_RespectsStatus(string status, bool expected)
    {
        var invoice = new Invoice { Status = status };

        Assert.Equal(expected, StripePaymentVerification.IsInvoicePaid(invoice));
    }
}
