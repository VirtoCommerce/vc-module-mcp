using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public class McpPublicOriginResolver(
    IOptions<McpOptions> options)
    : IMcpPublicOriginResolver
{
    public virtual Task<string> GetOriginAsync(HttpContext context, McpScope scope)
    {
        var configuredOrigin = GetConfiguredOrigin(scope);

        // At /mcp of a scope host, the resource is the host the request came to: a configured origin of another host
        // would advertise an address the client did not connect to. The configured origin is kept when it is that host,
        // so its scheme stays right behind a proxy that terminates TLS.
        if (context.GetMcpEndpointPath() == McpScope.HostEndpointPath && !IsOriginOfHost(configuredOrigin, context.Request.Host))
        {
            return Task.FromResult(GetRequestOrigin(context));
        }

        return Task.FromResult(!string.IsNullOrEmpty(configuredOrigin)
            ? configuredOrigin.TrimEnd('/')
            : GetRequestOrigin(context));
    }

    protected virtual string GetConfiguredOrigin(McpScope scope)
    {
        return scope != null && options.Value.Scopes.TryGetValue(scope.Name, out var scopeOptions) && !string.IsNullOrEmpty(scopeOptions.PublicOrigin)
            ? scopeOptions.PublicOrigin
            : options.Value.PublicOrigin;
    }

    protected static bool IsOriginOfHost(string origin, HostString host)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase) &&
            uri.Port == (host.Port ?? uri.Port);
    }

    protected static string GetRequestOrigin(HttpContext context)
    {
        var request = context.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
    }
}
