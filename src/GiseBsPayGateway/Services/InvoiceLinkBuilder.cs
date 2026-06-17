using GiseBsPayGateway.Configuration;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services;

public interface IInvoiceLinkBuilder
{
    string BuildDownloadPath(string invoiceCode);
    string BuildDownloadUrl(string invoiceCode);
}

public class InvoiceLinkBuilder : IInvoiceLinkBuilder
{
    private readonly DeploymentSettings _deployment;

    public InvoiceLinkBuilder(IOptions<DeploymentSettings> deployment) =>
        _deployment = deployment.Value;

    public string BuildDownloadPath(string invoiceCode) =>
        $"/api/invoices/{Uri.EscapeDataString(invoiceCode)}/download";

    public string BuildDownloadUrl(string invoiceCode)
    {
        var path = BuildDownloadPath(invoiceCode);
        if (string.IsNullOrWhiteSpace(_deployment.PublicBaseUrl))
        {
            return path;
        }

        return $"{_deployment.PublicBaseUrl.TrimEnd('/')}{path}";
    }
}
