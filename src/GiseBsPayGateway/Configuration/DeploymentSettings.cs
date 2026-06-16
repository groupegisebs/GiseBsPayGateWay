namespace GiseBsPayGateway.Configuration;

public class DeploymentSettings
{
    public const string SectionName = "Deployment";

    public string AppName { get; set; } = "GISEBS Pay Gateway";
    public string ServiceName { get; set; } = "gisebs-pay-gateway";
    public string AppRoot { get; set; } = "/opt/apps/gisebs-pay-gateway";
    public int ListenPort { get; set; } = 7843;
    public string? ConnectionString { get; set; }
}
