namespace GiseBsPayGateway.Configuration;

public static class ServerSecretsConfiguration
{
    public const string SecretsFileEnvVar = "GISEBSPAY_SECRETS_FILE";

    public static void AddServerSecretsFile(this ConfigurationManager configuration)
    {
        var secretsFile = ResolveSecretsFilePath(configuration);
        if (secretsFile is null || !File.Exists(secretsFile))
        {
            return;
        }

        configuration.AddJsonFile(secretsFile, optional: false, reloadOnChange: true);
    }

    public static string? ResolveSecretsFilePath(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable(SecretsFileEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv.Trim());
        }

        var appRoot = configuration[$"{DeploymentSettings.SectionName}:AppRoot"];
        if (string.IsNullOrWhiteSpace(appRoot))
        {
            return null;
        }

        return Path.Combine(appRoot.Trim(), "secrets.json");
    }
}
