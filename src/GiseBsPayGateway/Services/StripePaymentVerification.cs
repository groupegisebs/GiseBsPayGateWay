using Stripe;
using Stripe.Checkout;

namespace GiseBsPayGateway.Services;

public static class StripePaymentVerification
{
    public static bool IsCheckoutSessionPaymentConfirmed(Session session) =>
        string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);

    public static bool IsPaymentIntentSucceeded(string? status) =>
        string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);

    public static bool IsInvoicePaid(Invoice invoice) =>
        string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase);
}
