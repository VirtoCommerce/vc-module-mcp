using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddMcpScope(this IServiceCollection services, McpScope scope)
    {
        services.AddSingleton(scope);
        return services;
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
