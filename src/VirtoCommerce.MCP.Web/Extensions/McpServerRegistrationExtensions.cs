using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Middleware;

namespace VirtoCommerce.MCP.Web.Extensions;

public static class McpServerRegistrationExtensions
{
    public static IMcpServerBuilder AddScopedMcpServer(this IServiceCollection services)
    {
        var builder = services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = true;

                var configureSessionOptions = options.ConfigureSessionOptions;
                options.ConfigureSessionOptions = async (httpContext, serverOptions, cancellationToken) =>
                {
                    if (configureSessionOptions != null)
                    {
                        await configureSessionOptions(httpContext, serverOptions, cancellationToken);
                    }

                    await McpScopeSessionConfigurator.ConfigureAsync(httpContext, serverOptions);
                };
            })
            .AddAuthorizationFilters();

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
                });
            });
    }

    private static bool TryResolveScope(HttpContext context, IMcpScopeRegistry scopeRegistry)
    {
        var scope = scopeRegistry.FindScopeByPath(context.Request.Path);
        if (scope == null)
        {
            return false;
        }

        context.SetMcpScope(scope);
        return true;
    }
}
