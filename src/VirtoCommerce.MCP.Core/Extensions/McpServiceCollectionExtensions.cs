using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

    // Changes a scope another module declared, for the requests of that scope: its tools, challenge writer, origin
    // resolver and call filters (McpScopeBuilder). The scope itself stays as the module declared it.
    public static IServiceCollection ConfigureMcpScope(this IServiceCollection services, string scopeName, Action<McpScopeBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new McpScopeBuilder(services, new McpScope { Name = scopeName }));

        return services;
    }

    // Adds a module's step to the tool calls of one scope (McpScopes.Any for every scope). It runs inside the MCP
    // module's pipeline, after its checks and within its audit, and is resolved from the request's services. It also
    // runs around the execution of an approved change of the scope, and around the requester's dry run before it.
    public static IServiceCollection AddMcpCallToolFilter<TFilter>(this IServiceCollection services, string scopeName)
        where TFilter : class, IMcpCallToolFilter
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);

        services.AddHttpContextAccessor();
        services.AddScoped<TFilter>();
        services.AddSingleton(new McpCallToolFilterRegistration(scopeName, typeof(TFilter)));
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

    // Registers every tool, prompt and resource of the assembly of TAssemblyMarker, an empty class of the module's MCP
    // code (McpAssemblyMarker by convention). The [McpScope] of each type says which scopes serve it.
    public static IServiceCollection AddMcpCapabilities<TAssemblyMarker>(this IServiceCollection services)
    {
        return services.AddMcpCapabilities(typeof(TAssemblyMarker).Assembly);
    }

    public static IServiceCollection AddMcpCapabilities(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return services
            .AddMcpToolsFromAssembly(assembly)
            .AddMcpPromptsFromAssembly(assembly)
            .AddMcpResourcesFromAssembly(assembly);
    }

    // The tools, prompts and resources are serialized with McpSerialization.Options, the same for every module.
    public static IServiceCollection AddMcpTools<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TToolType>(this IServiceCollection services)
    {
        services.AddMcpServer().WithTools<TToolType>(McpSerialization.Options);
        return services;
    }

    public static IServiceCollection AddMcpTools(this IServiceCollection services, IEnumerable<Type> toolTypes)
    {
        services.AddMcpServer().WithTools(toolTypes, McpSerialization.Options);
        return services;
    }

    public static IServiceCollection AddMcpToolsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        services.AddMcpServer().WithToolsFromAssembly(assembly, McpSerialization.Options);
        return services;
    }

    public static IServiceCollection AddMcpPrompts<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TPromptType>(this IServiceCollection services)
    {
        services.AddMcpServer().WithPrompts<TPromptType>(McpSerialization.Options);
        return services;
    }

    public static IServiceCollection AddMcpPrompts(this IServiceCollection services, IEnumerable<Type> promptTypes)
    {
        services.AddMcpServer().WithPrompts(promptTypes, McpSerialization.Options);
        return services;
    }

    public static IServiceCollection AddMcpPromptsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        services.AddMcpServer().WithPromptsFromAssembly(assembly, McpSerialization.Options);
        return services;
    }

    public static IServiceCollection AddMcpResources<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TResourceType>(this IServiceCollection services)
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
