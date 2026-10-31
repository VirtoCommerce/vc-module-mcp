using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using VirtoCommerce.MCP.Web.Extensions;
using VirtoCommerce.Platform.Core.Modularity;

namespace VirtoCommerce.MCP.Web;

public class McpStartup : IPlatformStartup
{
    public void Configure(IApplicationBuilder app, IConfiguration config)
    {
        app.UseMcpScopes();
    }
}
