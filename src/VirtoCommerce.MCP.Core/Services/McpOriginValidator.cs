using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

// Allows the scope's own origin (IMcpPublicOriginResolver) and the origins of MCP:AllowedOrigins and the scope's
// AllowedOrigins. When the scope has no configured origin, its origin is the request's own, whose Host a rebinding page
// chooses, so it is trusted on a loopback host only, the Development case.
public class McpOriginValidator(
    IOptions<McpOptions> options)
    : IMcpOriginValidator
{
    public virtual async Task<bool> IsAllowedAsync(HttpContext context, McpScope scope, string origin)
    {
        var requestOrigin = Normalize(origin);
        if (requestOrigin == null)
        {
            return false;
        }

        if (GetAllowedOrigins(scope).Any(x => string.Equals(Normalize(x), requestOrigin, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var originResolver = context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope);
        if (!originResolver.IsOriginConfigured(context, scope) && !IsLoopback(context.Request.Host))
        {
            return false;
        }

        var scopeOrigin = await originResolver.GetOriginAsync(context, scope);

        return string.Equals(Normalize(scopeOrigin), requestOrigin, StringComparison.OrdinalIgnoreCase);
    }

    protected virtual IEnumerable<string> GetAllowedOrigins(McpScope scope)
    {
        var allowedOrigins = options.Value.AllowedOrigins ?? [];

        return scope != null && options.Value.Scopes.TryGetValue(scope.Name, out var scopeOptions) && scopeOptions?.AllowedOrigins != null
            ? allowedOrigins.Concat(scopeOptions.AllowedOrigins)
            : allowedOrigins;
    }

    // scheme://host[:port] without a default port, or null for a value that is not an http(s) origin, such as "null".
    protected static string Normalize(string origin)
    {
        return Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
    }

    protected static bool IsLoopback(HostString host)
    {
        var name = host.Host?.Trim('[', ']');

        return string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address));
    }
}
