using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Core.Extensions;

/// <summary>
/// Declares what a module's scope does differently from the MCP module, for the requests of that scope only. The
/// services are registered under the scope's key; other scopes keep the MCP module's ones, so an implementation never
/// has to check which scope it serves.
/// </summary>
/// <example>
/// <code>
/// services.AddMcpScope(new McpScope { Name = "ucp" }, scope => scope
///     .WithChallengeWriter&lt;UcpMcpChallengeWriter&gt;()
///     .WithPublicOriginResolver&lt;UcpMcpPublicOriginResolver&gt;()
///     .WithCallToolFilter&lt;UcpMcpCallToolFilter&gt;());
///
/// // A module built on UCP changes the scope UCP declared:
/// services.ConfigureMcpScope("ucp", scope => scope
///     .ReplaceTool&lt;MyProductTools&gt;("search_products")
///     .RemoveTool("list_regions"));
/// </code>
/// </example>
public class McpScopeBuilder
{
    private const string ServiceKeyPrefix = "VirtoCommerce.MCP.Scope:";

    public McpScopeBuilder(IServiceCollection services, McpScope scope)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(scope.Name);

        Services = services;
        Scope = scope;
    }

    public IServiceCollection Services { get; }

    public McpScope Scope { get; }

    public object ServiceKey => GetServiceKey(Scope.Name);

    // The bodies of the 401 and 403 responses that refuse a request of the scope.
    public virtual McpScopeBuilder WithChallengeWriter<TWriter>()
        where TWriter : McpChallengeWriter
    {
        Services.AddKeyedTransient<McpChallengeWriter, TWriter>(ServiceKey);
        return this;
    }

    // The public origin of the scope: its OAuth resource and the audience its tokens must have.
    public virtual McpScopeBuilder WithPublicOriginResolver<TResolver>()
        where TResolver : class, IMcpPublicOriginResolver
    {
        Services.AddKeyedTransient<IMcpPublicOriginResolver, TResolver>(ServiceKey);
        return this;
    }

    // The browser origins allowed to call the scope's endpoint, when MCP:AllowedOrigins and the scope's origin are not enough.
    public virtual McpScopeBuilder WithOriginValidator<TValidator>()
        where TValidator : class, IMcpOriginValidator
    {
        Services.AddKeyedTransient<IMcpOriginValidator, TValidator>(ServiceKey);
        return this;
    }

    // A step in the tool calls of the scope, inside the MCP module's checks and audit.
    public virtual McpScopeBuilder WithCallToolFilter<TFilter>()
        where TFilter : class, IMcpCallToolFilter
    {
        Services.AddMcpCallToolFilter<TFilter>(Scope.Name);
        return this;
    }

    // Stops serving a tool another module registered in the scope. Other scopes keep it.
    public virtual McpScopeBuilder RemoveTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        Services.AddSingleton(new McpToolOverride(Scope.Name, toolName, McpToolOverrideKind.Remove));
        return this;
    }

    // Serves the tool of this name from TToolType in the scope instead of the one registered before it, such as the one
    // of the module this module depends on. The tools of TToolType are registered as AddMcpTools registers them, and the
    // replacement must be in the scope ([McpScope]). When several modules replace one tool, the last one loaded wins.
    public virtual McpScopeBuilder ReplaceTool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] TToolType>(
        string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        Services.AddMcpTools<TToolType>();
        Services.AddSingleton(new McpToolOverride(Scope.Name, toolName, McpToolOverrideKind.Replace));
        return this;
    }

    // Scope names are compared ignoring case, so the key is too.
    public static object GetServiceKey(string scopeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);

        return ServiceKeyPrefix + scopeName.ToUpperInvariant();
    }
}
