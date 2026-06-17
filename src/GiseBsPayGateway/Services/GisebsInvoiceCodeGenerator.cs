using GiseBsPayGateway.Data;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Services;

public interface IGisebsInvoiceCodeGenerator
{
    Task<string> GenerateUniqueAsync(CancellationToken cancellationToken = default);
}

public class GisebsInvoiceCodeGenerator : IGisebsInvoiceCodeGenerator
{
    private readonly ApplicationDbContext _db;

    public GisebsInvoiceCodeGenerator(ApplicationDbContext db) => _db = db;

    public static string Format(DateTime utcNow, string suffix) =>
        $"G-{utcNow:yyyyMMdd}-{suffix}";

    public async Task<string> GenerateUniqueAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            var code = Format(DateTime.UtcNow, suffix);
            if (!await _db.PaymentInvoices.AnyAsync(x => x.InvoiceCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Impossible de générer un numéro de facture GISEBS unique.");
    }
}
