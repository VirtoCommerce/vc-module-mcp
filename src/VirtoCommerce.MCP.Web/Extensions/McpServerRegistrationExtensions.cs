using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Middleware;
using VirtoCommerce.MCP.Web.Services;

namespace VirtoCommerce.MCP.Web.Extensions;

// The MCP endpoints are endpoints of the platform pipeline, like its controllers: a request to /{scope}/mcp goes through
// the platform's error handling, security headers, authentication and account lockout. The scope is the endpoint's
// metadata (McpScopeEndpointMetadata), and two steps of the MCP module run around the platform's authentication:
//
//   UseRouting                    - matches the MCP endpoint of the scope
//   UseMcpScopeRequests           - body size limit, probe compatibility    (IPlatformStartup.ConfigureAfterRouting)
//   UseAuthentication
//   UseMcpScopeAuthentication     - the scope's token rules and challenges  (IPlatformStartup.ConfigureAfterAuthentication)
//   UseAuthorization
//   UseEndpoints(MapMcpScopes)    - the MCP server                          (IModule.PostInitialize)
public static class McpServerRegistrationExtensions
{
    public static IMcpServerBuilder AddScopedMcpServer(this IServiceCollection services)
    {
        var builder = services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .AddAuthorizationFilters()
            .WithMessageFilters(filters => filters.AddIncomingFilter(McpActivityStatusFilter.Create()));

        // The scope of a request leaves out the prompts and resources whose permissions the user does not have.
        services.AddAuthorizationCore();
        services.TryAddTransient<McpToolAuthorizationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<HttpServerTransportOptions>, McpScopeTransportSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<McpServerOptions>, McpCallToolFilterSetup>());

        return builder;
    }

    // Before authentication: limits the body before anything reads it, and answers JSON-only tools/list probes.
    public static IApplicationBuilder UseMcpScopeRequests(this IApplicationBuilder app)
    {
        return app.UseWhen(IsMcpRequest, branch =>
        {
            branch.UseMiddleware<McpRequestSizeLimitMiddleware>();
            branch.UseMiddleware<McpProbeCompatibilityMiddleware>();
        });
    }

    // After authentication: the request's user comes from its MCP access token only, and is challenged as the MCP
    // authorization flow expects.
    public static IApplicationBuilder UseMcpScopeAuthentication(this IApplicationBuilder app)
    {
        return app.UseWhen(IsMcpRequest, branch => branch.UseMiddleware<McpScopeAuthenticationMiddleware>());
    }

    // One endpoint per scope rather than a "/{scopeName}/mcp" template: the route is the request's name in traces and
    // Application Insights (POST /admin/mcp), so each scope is told apart. A scope with hosts is also served at /mcp on them.
    public static IEndpointRouteBuilder MapMcpScopes(this IEndpointRouteBuilder endpoints)
    {
        var scopeRegistry = endpoints.ServiceProvider.GetRequiredService<IMcpScopeRegistry>();
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<McpOptions>>().Value;

        foreach (var scope in scopeRegistry.Scopes)
        {
            endpoints.MapMcp(scope.Path)
                .WithMetadata(new McpScopeEndpointMetadata(scope, scope.Path));

            var hosts = options.Scopes.TryGetValue(scope.Name, out var scopeOptions)
                ? scopeOptions.Hosts?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray()
                : null;

            if (hosts is { Length: > 0 })
            {
                endpoints.MapMcp(McpScope.HostEndpointPath)
                    .RequireHost(hosts)
                    .WithMetadata(new McpScopeEndpointMetadata(scope, McpScope.HostEndpointPath));
            }
        }

        return endpoints;
    }

    private static bool IsMcpRequest(HttpContext context)
    {
        return context.GetMcpScopeEndpoint() != null;
    }
}
