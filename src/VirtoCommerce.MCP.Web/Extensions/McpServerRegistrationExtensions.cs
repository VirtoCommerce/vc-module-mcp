using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Middleware;

namespace VirtoCommerce.MCP.Web.Extensions;

public static class McpServerRegistrationExtensions
{
    public static IMcpServerBuilder AddScopedMcpServer(this IServiceCollection services)
    {
        var builder = services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .AddAuthorizationFilters();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<HttpServerTransportOptions>, McpScopeTransportSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<McpServerOptions>, McpCallToolFilterSetup>());

        return builder;
    }

    public static IApplicationBuilder UseMcpScopes(this IApplicationBuilder app)
    {
        var scopeRegistry = app.ApplicationServices.GetRequiredService<IMcpScopeRegistry>();

        return app.MapWhen(
            context => TryResolveScope(context, scopeRegistry),
            branch =>
            {
                branch.UseMiddleware<McpProbeCompatibilityMiddleware>();
                branch.UseRouting();
                branch.UseAuthentication();
                branch.UseMiddleware<McpScopeAuthenticationMiddleware>();
                branch.UseAuthorization();
                branch.UseEndpoints(endpoints =>
                {
                    endpoints.MapMcp("/{scopeName}/mcp");
                    endpoints.MapMcp(McpScope.HostEndpointPath);
                });
            });
    }

    private static bool TryResolveScope(HttpContext context, IMcpScopeRegistry scopeRegistry)
    {
        var scope = scopeRegistry.FindScope(context.Request.Host, context.Request.Path);
        if (scope == null)
        {
            return false;
        }

        context.SetMcpScope(scope);
        context.SetMcpEndpointPath(McpScopeRegistry.IsHostEndpointPath(context.Request.Path) ? McpScope.HostEndpointPath : scope.Path);
        return true;
    }
}
