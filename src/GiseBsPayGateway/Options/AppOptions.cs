namespace GiseBsPayGateway.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
}

public class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    public string HeaderName { get; set; } = "X-Api-Key";
    public string AppCodeHeaderName { get; set; } = "X-App-Code";
}

public class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@gisebs.com";
    public string AdminPassword { get; set; } = "ChangeMe123!";
    public string TestEmail { get; set; } = "test@gisebs.com";
    public string TestPassword { get; set; } = "Test123!";
}
