using GiseBsPayGateway.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GiseBsPayGateway.Services;

public interface IInvoicePdfGenerator
{
    byte[] Generate(PaymentInvoice invoice, PaymentTransaction? payment);
}

public class InvoicePdfGenerator : IInvoicePdfGenerator
{
    static InvoicePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentInvoice invoice, PaymentTransaction? payment)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(column =>
                {
                    column.Item().Text("GISEBS Pay Gateway").FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                    column.Item().Text("Facture de paiement").FontSize(12).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(20).Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text($"N° facture : {invoice.InvoiceCode}").Bold();
                            left.Item().Text($"Date : {invoice.InvoiceDate:yyyy-MM-dd HH:mm} UTC");
                            if (invoice.PaidAt.HasValue)
                            {
                                left.Item().Text($"Payé le : {invoice.PaidAt:yyyy-MM-dd HH:mm} UTC");
                            }
                        });

                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().Text("Statut").FontColor(Colors.Grey.Darken1);
                            right.Item().Text(invoice.Status.ToString()).Bold();
                        });
                    });

                    column.Item().Background(Colors.Grey.Lighten4).Padding(12).Column(box =>
                    {
                        box.Item().Text("Client").Bold();
                        box.Item().Text(invoice.CustomerName ?? invoice.CustomerCode);
                        box.Item().Text(invoice.CustomerEmail);
                        box.Item().Text($"Code client : {invoice.CustomerCode}");
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Blue.Darken2).Padding(6)
                                .Text("Description").FontColor(Colors.White).Bold();
                            header.Cell().Background(Colors.Blue.Darken2).Padding(6)
                                .Text("Plan").FontColor(Colors.White).Bold();
                            header.Cell().Background(Colors.Blue.Darken2).Padding(6)
                                .AlignRight().Text("Montant").FontColor(Colors.White).Bold();
                        });

                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                            .Text($"{invoice.ProductName} ({invoice.ProductCode})");
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                            .Text($"{invoice.PlanName} ({invoice.PlanCode})");
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                            .AlignRight().Text($"{FormatSubtotal(invoice):N2} {invoice.Currency.ToUpperInvariant()}");
                    });

                    column.Item().AlignRight().Column(totals =>
                    {
                        totals.Spacing(4);
                        totals.Item().Text($"Sous-total : {FormatSubtotal(invoice):N2} {invoice.Currency.ToUpperInvariant()}");
                        if (invoice.TaxAmount is > 0)
                        {
                            totals.Item().Text($"Taxes : {invoice.TaxAmount:N2} {invoice.Currency.ToUpperInvariant()}");
                        }

                        totals.Item().Text($"Total : {FormatTotal(invoice):N2} {invoice.Currency.ToUpperInvariant()}")
                            .FontSize(14).Bold();

                        if (invoice.StripeFee is > 0 || invoice.NetAmount is > 0)
                        {
                            totals.Item().PaddingTop(6).Text("Stripe").Bold().FontSize(11);
                            if (invoice.StripeFee is > 0)
                            {
                                totals.Item().Text($"Frais Stripe : {invoice.StripeFee:N2} {invoice.Currency.ToUpperInvariant()}");
                            }

                            if (invoice.NetAmount is > 0)
                            {
                                totals.Item().Text($"Net reçu : {invoice.NetAmount:N2} {invoice.Currency.ToUpperInvariant()}");
                            }
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(invoice.BillingCountry))
                    {
                        column.Item().Text(text =>
                        {
                            text.Span("Adresse de facturation : ");
                            text.Span(invoice.BillingCountry);
                            if (!string.IsNullOrWhiteSpace(invoice.BillingState))
                            {
                                text.Span($" / {invoice.BillingState}");
                            }
                        });
                    }

                    column.Item().PaddingTop(8).Column(refs =>
                    {
                        refs.Item().Text("Références").Bold();
                        if (!string.IsNullOrWhiteSpace(payment?.PaymentCode))
                        {
                            refs.Item().Text($"Code paiement : {payment.PaymentCode}");
                        }

                        if (!string.IsNullOrWhiteSpace(invoice.StripePaymentIntentId))
                        {
                            refs.Item().Text($"Stripe Payment Intent : {invoice.StripePaymentIntentId}");
                        }

                        if (!string.IsNullOrWhiteSpace(invoice.StripeCheckoutSessionId))
                        {
                            refs.Item().Text($"Stripe Checkout Session : {invoice.StripeCheckoutSessionId}");
                        }

                        if (!string.IsNullOrWhiteSpace(invoice.StripeInvoiceId))
                        {
                            refs.Item().Text($"Stripe Invoice : {invoice.StripeInvoiceId}");
                        }

                        if (!string.IsNullOrWhiteSpace(invoice.StripeBalanceTransactionId))
                        {
                            refs.Item().Text($"Stripe Balance Transaction : {invoice.StripeBalanceTransactionId}");
                        }
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Document généré par GISEBS Pay Gateway — ");
                    text.Span(invoice.InvoiceCode).Bold();
                });
            });
        }).GeneratePdf();
    }

    private static decimal FormatSubtotal(PaymentInvoice invoice) =>
        invoice.AmountSubtotal ?? invoice.Amount;

    private static decimal FormatTotal(PaymentInvoice invoice) =>
        invoice.GrossAmount ?? invoice.Amount;
}
