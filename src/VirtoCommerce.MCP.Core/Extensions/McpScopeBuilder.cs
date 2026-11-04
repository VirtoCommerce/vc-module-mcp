using System;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Core.Extensions;

// Declares what a module's scope does differently from the MCP module, for the requests of that scope only:
//
//   services.AddMcpScope(new McpScope { Name = "ucp", ... }, scope => scope
//       .WithChallengeWriter<UcpMcpChallengeWriter>()
//       .WithPublicOriginResolver<UcpMcpPublicOriginResolver>()
//       .WithCallToolFilter<UcpMcpCallToolFilter>());
//
// The services are registered under the scope's key; other scopes keep the MCP module's ones, so an implementation
// never has to check which scope it serves.
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

    // A step in the tool calls of the scope, inside the MCP module's checks and audit.
    public virtual McpScopeBuilder WithCallToolFilter<TFilter>()
        where TFilter : class, IMcpCallToolFilter
    {
        Services.AddMcpCallToolFilter<TFilter>(Scope.Name);
        return this;
    }

    // Scope names are compared ignoring case, so the key is too.
    public static object GetServiceKey(string scopeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);

        return ServiceKeyPrefix + scopeName.ToUpperInvariant();
    }
}
