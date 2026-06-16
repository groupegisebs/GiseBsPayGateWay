using GiseBsPayGateway.Entities;

namespace GiseBsPayGateway.Authentication;

public class ClientApplicationContext
{
    public ClientApplication Application { get; set; } = null!;
    public ApplicationApiKey ApiKey { get; set; } = null!;
}
