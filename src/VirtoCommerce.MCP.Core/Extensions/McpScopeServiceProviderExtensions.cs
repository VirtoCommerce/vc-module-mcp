using System;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpScopeServiceProviderExtensions
{
    // The service the scope declared with McpScopeBuilder, or the MCP module's one.
    public static TService GetMcpScopeService<TService>(this IServiceProvider serviceProvider, McpScope scope)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var scopeService = string.IsNullOrEmpty(scope?.Name)
            ? null
            : serviceProvider.GetKeyedService<TService>(McpScopeBuilder.GetServiceKey(scope.Name));

        return scopeService ?? serviceProvider.GetRequiredService<TService>();
    }
}
