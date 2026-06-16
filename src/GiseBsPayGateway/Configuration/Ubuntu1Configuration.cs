namespace GiseBsPayGateway.Configuration;

public static class Ubuntu1Configuration
{
    private static readonly (string EnvVar, string ConfigKey)[] Mappings =
    [
        ("UBUNTU1_CONNECTION_STRING", "ConnectionStrings:DefaultConnection"),
        ("UBUNTU1_CONNECTION_STRING", $"{DeploymentSettings.SectionName}:ConnectionString"),
        ("UBUNTU1_APP_NAME", $"{DeploymentSettings.SectionName}:AppName"),
        ("UBUNTU1_SERVICE_NAME", $"{DeploymentSettings.SectionName}:ServiceName"),
        ("UBUNTU1_APP_ROOT", $"{DeploymentSettings.SectionName}:AppRoot"),
        ("UBUNTU1_LISTEN_PORT", $"{DeploymentSettings.SectionName}:ListenPort"),
    ];

    public static void AddUbuntu1Overrides(this ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (envVar, configKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrWhiteSpace(value)) continue;
            overrides[configKey] = value.Trim();
        }

        if (overrides.Count > 0)
            configuration.AddInMemoryCollection(overrides);
    }

    public static void ApplyListenUrl(this WebApplicationBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            return;

        var port = builder.Configuration.GetValue<int?>($"{DeploymentSettings.SectionName}:ListenPort");
        if (port is > 0)
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    }
}
