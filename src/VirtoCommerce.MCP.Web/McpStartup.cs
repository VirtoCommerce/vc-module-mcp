using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using VirtoCommerce.MCP.Web.Extensions;
using VirtoCommerce.Platform.Core.Modularity;

namespace VirtoCommerce.MCP.Web;

// The MCP steps of the platform pipeline, around its authentication. The endpoints are mapped in Module.PostInitialize.
public class McpStartup : IPlatformStartup
{
    public void ConfigureAfterRouting(IApplicationBuilder app, IConfiguration config)
    {
        app.UseMcpScopeRequests();
    }

    public void ConfigureAfterAuthentication(IApplicationBuilder app, IConfiguration config)
    {
        app.UseMcpScopeAuthentication();
    }
}
