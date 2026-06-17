using GiseBsPayGateway.Configuration;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public interface IInvoiceFileStorage
{
    Task<string> SavePdfAsync(string invoiceCode, byte[] pdfBytes, CancellationToken cancellationToken = default);
    string? ResolveFullPath(string? relativePath);
}

public class InvoiceFileStorage : IInvoiceFileStorage
{
    private readonly string _appRoot;

    public InvoiceFileStorage(IOptions<DeploymentSettings> deployment)
    {
        _appRoot = deployment.Value.AppRoot;
    }

    public async Task<string> SavePdfAsync(string invoiceCode, byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        var invoicesDir = Path.Combine(_appRoot, "invoices");
        Directory.CreateDirectory(invoicesDir);

        var fileName = $"{SanitizeFileName(invoiceCode)}.pdf";
        var relativePath = Path.Combine("invoices", fileName).Replace('\\', '/');
        var fullPath = Path.Combine(_appRoot, relativePath);
        await File.WriteAllBytesAsync(fullPath, pdfBytes, cancellationToken);
        return relativePath;
    }

    public string? ResolveFullPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(_appRoot, relativePath));
        var invoicesRoot = Path.GetFullPath(Path.Combine(_appRoot, "invoices"));
        if (!fullPath.StartsWith(invoicesRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(fullPath) ? fullPath : null;
    }

    private static string SanitizeFileName(string invoiceCode) =>
        new(invoiceCode.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
}
