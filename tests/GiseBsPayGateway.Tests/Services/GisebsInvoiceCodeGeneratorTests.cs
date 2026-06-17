using GiseBsPayGateway.Services;

namespace GiseBsPayGateway.Tests.Services;

public class GisebsInvoiceCodeGeneratorTests
{
    [Fact]
    public void Format_UsesGisebsPattern()
    {
        var code = GisebsInvoiceCodeGenerator.Format(
            new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc),
            "AB12CD34EF");

        Assert.Equal("G-20260617-AB12CD34EF", code);
    }
}
