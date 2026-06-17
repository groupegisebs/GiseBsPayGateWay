using GiseBsPayGateway.Entities;
using Stripe.Checkout;

namespace GiseBsPayGateway.Services;

public static class StripeCheckoutFinancials
{
    public static void ApplySessionTaxToPayment(PaymentTransaction payment, Session session)
    {
        long? subtotalCents = session.AmountSubtotal is > 0 ? session.AmountSubtotal : null;
        long? totalCents = session.AmountTotal is > 0 ? session.AmountTotal : null;

        if (subtotalCents is > 0)
        {
            payment.AmountSubtotal = subtotalCents.Value / 100m;
        }

        if (session.TotalDetails?.AmountTax is long taxCents && taxCents > 0)
        {
            payment.TaxAmount = taxCents / 100m;
        }
        else if (totalCents is > 0 && subtotalCents is > 0 && totalCents > subtotalCents)
        {
            payment.TaxAmount = (totalCents.Value - subtotalCents.Value) / 100m;
        }

        if (totalCents is > 0)
        {
            var gross = totalCents.Value / 100m;
            if (!payment.GrossAmount.HasValue || payment.GrossAmount.Value != gross)
            {
                payment.GrossAmount = gross;
            }
        }

        var address = session.CustomerDetails?.Address;
        payment.BillingCountry ??= address?.Country;
        payment.BillingState ??= address?.State;
    }

    public static void ApplyBalanceTransactionToPayment(
        PaymentTransaction payment,
        StripeBalanceTransactionDetails? details)
    {
        if (details is null)
        {
            return;
        }

        payment.StripeFee = details.Fee;
        payment.NetAmount = details.Net;
        payment.StripeBalanceTransactionId = details.BalanceTransactionId;

        if (details.GrossAmount != payment.Amount)
        {
            payment.GrossAmount = details.GrossAmount;
        }
    }

    public static void CopyPaymentFinancialsToInvoice(PaymentInvoice invoice, PaymentTransaction payment)
    {
        invoice.AmountSubtotal = payment.AmountSubtotal;
        invoice.TaxAmount = payment.TaxAmount;
        invoice.GrossAmount = payment.GrossAmount;
        invoice.StripeFee = payment.StripeFee;
        invoice.NetAmount = payment.NetAmount;
        invoice.StripeBalanceTransactionId = payment.StripeBalanceTransactionId;
        invoice.BillingCountry = payment.BillingCountry;
        invoice.BillingState = payment.BillingState;
        invoice.Amount = ResolveCustomerTotal(payment);
    }

    public static decimal ResolveCustomerTotal(PaymentTransaction payment)
    {
        if (payment.GrossAmount.HasValue)
        {
            return payment.GrossAmount.Value;
        }

        if (payment.AmountSubtotal.HasValue && payment.TaxAmount.HasValue)
        {
            return payment.AmountSubtotal.Value + payment.TaxAmount.Value;
        }

        return payment.Amount;
    }
}
