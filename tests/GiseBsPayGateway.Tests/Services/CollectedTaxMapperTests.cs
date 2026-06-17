using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Services;
using Stripe;
using Stripe.Checkout;

namespace GiseBsPayGateway.Tests.Services;

public class CollectedTaxMapperTests
{
    [Fact]
    public void MapFromCheckoutSession_Quebec_ReturnsGstAndQstWithAmounts()
    {
        var session = new Session
        {
            AmountSubtotal = 10000,
            AmountTotal = 11498,
            TotalDetails = new SessionTotalDetails
            {
                AmountTax = 1498,
                Breakdown = new SessionTotalDetailsBreakdown
                {
                    Taxes =
                    [
                        new SessionTotalDetailsBreakdownTax
                        {
                            Amount = 500,
                            TaxableAmount = 10000,
                            Rate = new TaxRate
                            {
                                DisplayName = "GST",
                                Percentage = 5.0m,
                                TaxType = "gst",
                                Country = "CA"
                            }
                        },
                        new SessionTotalDetailsBreakdownTax
                        {
                            Amount = 998,
                            TaxableAmount = 10000,
                            Rate = new TaxRate
                            {
                                DisplayName = "QST",
                                Percentage = 9.975m,
                                TaxType = "qst",
                                Country = "CA",
                                State = "QC"
                            }
                        }
                    ]
                }
            },
            CustomerDetails = new SessionCustomerDetails
            {
                Address = new Address
                {
                    Line1 = "1200 rue Edison",
                    City = "Québec",
                    State = "QC",
                    PostalCode = "G3K 0P6",
                    Country = "CA"
                }
            }
        };

        var lines = CollectedTaxMapper.MapFromCheckoutSession(session);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Code == "ca_gst" && l.Rate == 0.05m && l.Amount == 5.00m && l.Type == "federal");
        Assert.Contains(lines, l => l.Code == "ca_qst" && l.Rate == 0.09975m && l.Amount == 9.98m && l.Type == "provincial");
    }

    [Fact]
    public void MapFromCheckoutSession_FallbackAggregate_WhenOnlyAmountTaxPresent()
    {
        var session = new Session
        {
            AmountSubtotal = 10000,
            AmountTotal = 11300,
            TotalDetails = new SessionTotalDetails { AmountTax = 1300 },
            CustomerDetails = new SessionCustomerDetails
            {
                Address = new Address { Country = "CA", State = "ON" }
            }
        };

        var lines = CollectedTaxMapper.MapFromCheckoutSession(session);

        Assert.Single(lines);
        Assert.Equal(13.00m, lines[0].Amount);
        Assert.Equal(0.13m, lines[0].Rate);
        Assert.Equal("ca_tax", lines[0].Code);
    }

    [Fact]
    public void ApplyBillingAddress_CopiesAllFields()
    {
        var record = new CollectedTaxRecord();
        var address = new Address
        {
            Line1 = "1200 rue Edison",
            Line2 = "Suite 100",
            City = "Québec",
            State = "QC",
            PostalCode = "G3K 0P6",
            Country = "ca"
        };

        CollectedTaxMapper.ApplyBillingAddress(record, address);

        Assert.Equal("1200 rue Edison", record.BillingLine1);
        Assert.Equal("Suite 100", record.BillingLine2);
        Assert.Equal("Québec", record.BillingCity);
        Assert.Equal("QC", record.BillingState);
        Assert.Equal("G3K 0P6", record.BillingPostalCode);
        Assert.Equal("CA", record.BillingCountry);
    }

    [Fact]
    public void ToLineDtos_PreservesSortOrder()
    {
        var lines = new List<CollectedTaxLine>
        {
            new() { SortOrder = 1, Code = "ca_qst", Name = "QST", Rate = 0.09975m, Amount = 9.98m, Type = "provincial" },
            new() { SortOrder = 0, Code = "ca_gst", Name = "GST", Rate = 0.05m, Amount = 5.00m, Type = "federal" }
        };

        var dtos = CollectedTaxMapper.ToLineDtos(lines);

        Assert.Equal("ca_gst", dtos[0].Code);
        Assert.Equal("ca_qst", dtos[1].Code);
        Assert.Equal(5.00m, dtos[0].Amount);
    }
}
