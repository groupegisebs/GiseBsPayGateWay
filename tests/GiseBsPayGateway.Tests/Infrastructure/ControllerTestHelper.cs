using GiseBsPayGateway.Authentication;
using GiseBsPayGateway.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Tests.Infrastructure;

public static class ControllerTestHelper
{
    public static void SetClientApplicationContext(ControllerBase controller, ClientApplication app, ApplicationApiKey apiKey)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[nameof(ClientApplicationContext)] = new ClientApplicationContext
        {
            Application = app,
            ApiKey = apiKey
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }
}
