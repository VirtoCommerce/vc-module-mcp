using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddMcpScope(this IServiceCollection services, McpScope scope)
    {
        services.AddSingleton(scope);
        return services;
    }

    // Adds the scope with the services it replaces for its own requests (see McpScopeBuilder).
    public static IServiceCollection AddMcpScope(this IServiceCollection services, McpScope scope, Action<McpScopeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new McpScopeBuilder(services, scope);
        services.AddMcpScope(scope);
        configure(builder);

        return services;
    }

    // Adds a module's step to the tool calls of one scope (McpScopes.Any for every scope). It runs inside the MCP
    // module's pipeline, after its checks and within its audit, and is resolved from the request's services.
    public static IServiceCollection AddMcpCallToolFilter<TFilter>(this IServiceCollection services, string scopeName)
        where TFilter : class, IMcpCallToolFilter
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);

        services.AddHttpContextAccessor();
        services.AddScoped<TFilter>();
        services.AddMcpServer().WithRequestFilters(filters =>
            filters.AddCallToolFilter(next => (context, cancellationToken) => IsInScope(context.Services, scopeName)
                ? context.Services.GetRequiredService<TFilter>().InvokeAsync(next, context, cancellationToken)
                : next(context, cancellationToken)));

        return services;
    }

    private static bool IsInScope(IServiceProvider services, string scopeName)
    {
        var scope = services?.GetService<IHttpContextAccessor>()?.HttpContext?.GetMcpScope();

        return scope != null && (scopeName == McpScopes.Any || string.Equals(scope.Name, scopeName, StringComparison.OrdinalIgnoreCase));
    }

    public static IServiceCollection AddMcpTools<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TToolType>(
        this IServiceCollection services,
        JsonSerializerOptions serializerOptions = null)
    {
        services.AddMcpServer().WithTools<TToolType>(serializerOptions);
        return services;
    }

    public static IServiceCollection AddMcpTools(this IServiceCollection services, IEnumerable<Type> toolTypes, JsonSerializerOptions serializerOptions = null)
    {
        services.AddMcpServer().WithTools(toolTypes, serializerOptions);
        return services;
    }

    public static IServiceCollection AddMcpToolsFromAssembly(this IServiceCollection services, Assembly assembly, JsonSerializerOptions serializerOptions = null)
    {
        services.AddMcpServer().WithToolsFromAssembly(assembly, serializerOptions);
        return services;
    }

    public static IServiceCollection AddMcpPrompts<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TPromptType>(
        this IServiceCollection services,
        JsonSerializerOptions serializerOptions = null)
    {
        services.AddMcpServer().WithPrompts<TPromptType>(serializerOptions);
        return services;
    }

    public static IServiceCollection AddMcpPrompts(this IServiceCollection services, IEnumerable<Type> promptTypes, JsonSerializerOptions serializerOptions = null)
    {
        services.AddMcpServer().WithPrompts(promptTypes, serializerOptions);
        return services;
    }

    public static IServiceCollection AddMcpPromptsFromAssembly(this IServiceCollection services, Assembly assembly, JsonSerializerOptions serializerOptions = null)
    {
        services.AddMcpServer().WithPromptsFromAssembly(assembly, serializerOptions);
        return services;
    }

    public static IServiceCollection AddMcpResources<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TResourceType>(
        this IServiceCollection services)
    {
        services.AddMcpServer().WithResources<TResourceType>();
        return services;
    }

    public static IServiceCollection AddMcpResources(this IServiceCollection services, IEnumerable<Type> resourceTypes)
    {
        services.AddMcpServer().WithResources(resourceTypes);
        return services;
    }

    public static IServiceCollection AddMcpResourcesFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        services.AddMcpServer().WithResourcesFromAssembly(assembly);
        return services;
    }
}
