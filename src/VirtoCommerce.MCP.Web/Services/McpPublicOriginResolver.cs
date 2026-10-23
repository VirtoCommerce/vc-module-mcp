using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Web.Services;

public class McpPublicOriginResolver(
    IOptions<McpOptions> options)
    : IMcpPublicOriginResolver
{
    public virtual string GetOrigin(HttpContext context, McpScope scope)
    {
        var configuredOrigin = scope != null && options.Value.Scopes.TryGetValue(scope.Name, out var scopeOptions) && !string.IsNullOrEmpty(scopeOptions.PublicOrigin)
            ? scopeOptions.PublicOrigin
            : options.Value.PublicOrigin;

        if (!string.IsNullOrEmpty(configuredOrigin))
        {
            return configuredOrigin.TrimEnd('/');
        }

        var request = context.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
    }
}
