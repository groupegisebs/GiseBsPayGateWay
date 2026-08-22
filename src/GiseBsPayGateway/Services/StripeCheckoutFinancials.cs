using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Entities;
using Stripe;
using Stripe.Checkout;

namespace GiseBsPayGateway.Services;

public static class StripeCheckoutFinancials
{
    public static bool NeedsTaxAmount(PaymentTransaction payment) =>
        payment.TaxAmount is null or 0;

    public static bool NeedsFinancialTaxFields(PaymentTransaction payment) =>
        NeedsTaxAmount(payment)
        || !payment.AmountSubtotal.HasValue
        || !payment.GrossAmount.HasValue;

    public static void ApplySessionTaxToPayment(PaymentTransaction payment, Session session)
    {
        long? subtotalMinor = session.AmountSubtotal is > 0 ? session.AmountSubtotal : null;
        long? totalMinor = session.AmountTotal is > 0 ? session.AmountTotal : null;

        if (subtotalMinor is > 0)
        {
            payment.AmountSubtotal = CatalogOptions.FromStripeUnitAmount(subtotalMinor.Value, payment.Currency);
        }

        if (session.TotalDetails?.AmountTax is long taxMinor && taxMinor > 0)
        {
            payment.TaxAmount = CatalogOptions.FromStripeUnitAmount(taxMinor, payment.Currency);
        }
        else if (totalMinor is > 0 && subtotalMinor is > 0 && totalMinor > subtotalMinor)
        {
            payment.TaxAmount = CatalogOptions.FromStripeUnitAmount(
                totalMinor.Value - subtotalMinor.Value,
                payment.Currency);
        }

        if (totalMinor is > 0)
        {
            var gross = CatalogOptions.FromStripeUnitAmount(totalMinor.Value, payment.Currency);
            if (!payment.GrossAmount.HasValue || payment.GrossAmount.Value != gross)
            {
                payment.GrossAmount = gross;
            }
        }

        var address = session.CustomerDetails?.Address;
        payment.BillingCountry ??= address?.Country;
        payment.BillingState ??= address?.State;
    }

    public static void ApplyStripeInvoiceTaxToPayment(PaymentTransaction payment, Invoice invoice)
    {
        long? subtotalMinor = invoice.TotalExcludingTax is > 0 ? invoice.TotalExcludingTax : null;
        subtotalMinor ??= invoice.Subtotal is > 0 ? invoice.Subtotal : null;

        if (subtotalMinor is > 0)
        {
            payment.AmountSubtotal ??= CatalogOptions.FromStripeUnitAmount(subtotalMinor.Value, payment.Currency);
        }

        if (invoice.TotalTaxes is { Count: > 0 })
        {
            var tax = CatalogOptions.FromStripeUnitAmount(
                invoice.TotalTaxes.Sum(x => x.Amount),
                payment.Currency);
            if (tax > 0)
            {
                payment.TaxAmount = tax;
            }
        }
        else if (invoice.Total is > 0 && subtotalMinor is > 0 && invoice.Total > subtotalMinor)
        {
            payment.TaxAmount = CatalogOptions.FromStripeUnitAmount(
                invoice.Total - subtotalMinor.Value,
                payment.Currency);
        }

        if (invoice.Total is > 0)
        {
            payment.GrossAmount = CatalogOptions.FromStripeUnitAmount(invoice.Total, payment.Currency);
        }

        payment.BillingCountry ??= invoice.CustomerAddress?.Country;
        payment.BillingState ??= invoice.CustomerAddress?.State;
    }

    public static void ApplyBalanceTransactionToPayment(
        PaymentTransaction payment,
        StripeBalanceTransactionDetails? details)
    {
        if (details is null)
        {
            return;
        }

        payment.StripeFee ??= details.Fee;
        payment.NetAmount ??= details.Net;
        payment.StripeBalanceTransactionId ??= details.BalanceTransactionId;

        if (details.GrossAmount != payment.Amount)
        {
            payment.GrossAmount ??= details.GrossAmount;
        }
    }

    public static void ApplyCollectedTaxRecordToPayment(PaymentTransaction payment, CollectedTaxRecord record)
    {
        if (record.AmountSubtotal > 0)
        {
            payment.AmountSubtotal ??= record.AmountSubtotal;
        }

        if (record.TaxAmountTotal > 0)
        {
            payment.TaxAmount = record.TaxAmountTotal;
        }

        if (record.GrossAmount > 0)
        {
            payment.GrossAmount ??= record.GrossAmount;
        }

        payment.BillingCountry ??= record.BillingCountry;
        payment.BillingState ??= record.BillingState;
    }

    public static bool NeedsFinancialBackfill(PaymentTransaction payment) =>
        string.IsNullOrWhiteSpace(payment.StripePaymentIntentId)
        || NeedsTaxAmount(payment)
        || !payment.StripeFee.HasValue
        || !payment.NetAmount.HasValue;

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
