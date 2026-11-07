using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

namespace VirtoCommerce.MCP.Web.Filters;

// Runs after every Configure of the transport options, so a module that sets its own ConfigureSessionOptions later
// keeps its handler and cannot switch the scope isolation off.
public class McpScopeTransportSetup : IPostConfigureOptions<HttpServerTransportOptions>
{
    public void PostConfigure(string name, HttpServerTransportOptions options)
    {
        var configureSessionOptions = options.ConfigureSessionOptions;

        options.ConfigureSessionOptions = async (httpContext, serverOptions, cancellationToken) =>
        {
            if (configureSessionOptions != null)
            {
                await configureSessionOptions(httpContext, serverOptions, cancellationToken);
            }

            await McpScopeSessionConfigurator.ConfigureAsync(httpContext, serverOptions);
        };
    }
}
