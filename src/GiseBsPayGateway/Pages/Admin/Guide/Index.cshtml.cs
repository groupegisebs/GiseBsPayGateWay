using GiseBsPayGateway.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Pages.Admin.Guide;

public class IndexModel : PageModel
{
    private readonly DeploymentSettings _deployment;

    public IndexModel(IOptions<DeploymentSettings> deployment)
    {
        _deployment = deployment.Value;
    }

    public string BaseUrl { get; private set; } = string.Empty;
    public int ListenPort { get; private set; }

    public void OnGet()
    {
        BaseUrl = $"{Request.Scheme}://{Request.Host}";
        ListenPort = _deployment.ListenPort;
    }
}
