using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Services;

// The MCP endpoints of the enabled scopes, so that administrators pick them as resources of OAuth applications in the
// back office: /{scope}/mcp at the scope's public origin, and /mcp on each host configured for the scope.
public class McpOAuthResourceProvider(
    IMcpScopeRegistry scopeRegistry,
    IOptions<McpOptions> options)
    : IOAuthResourceProvider
{
    public const string ResourceKind = "mcp";

    public virtual async Task<IList<OAuthResource>> GetResourcesAsync(OAuthResourceContext context, CancellationToken cancellationToken)
    {
        var result = new List<OAuthResource>();

        // The public origin of a scope may come from the request; without one the endpoints are not listed
        var httpContext = context?.HttpContext;
        if (httpContext == null)
        {
            return result;
        }

        foreach (var scope in scopeRegistry.Scopes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var originResolver = httpContext.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope);
            var origin = await originResolver.GetOriginAsync(httpContext, scope);

            result.Add(CreateResource(scope, origin + scope.Path));

            if (options.Value.Scopes.TryGetValue(scope.Name, out var scopeOptions))
            {
                result.AddRange(scopeOptions.Hosts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => CreateResource(scope, GetHostOrigin(x.Trim(), httpContext) + McpScope.HostEndpointPath)));
            }
        }

        return result;
    }

    private static OAuthResource CreateResource(McpScope scope, string uri)
    {
        return new OAuthResource
        {
            Uri = uri,
            Title = scope.Title ?? scope.Name,
            Kind = ResourceKind,
        };
    }

    // A configured host has no scheme: HTTPS, unless it is the request's own host or a loopback host
    private static string GetHostOrigin(string host, HttpContext httpContext)
    {
        var hostString = HostString.FromUriComponent(host);
        var isLocal = string.Equals(hostString.Host, httpContext.Request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
            hostString.Host is "localhost" or "127.0.0.1" or "[::1]";

        return $"{(isLocal ? httpContext.Request.Scheme : Uri.UriSchemeHttps)}://{hostString}";
    }
}
