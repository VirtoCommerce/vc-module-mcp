using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpHttpContextExtensions
{
    private static readonly object _scopeKey = new();
    private static readonly object _endpointPathKey = new();

    public static McpScope GetMcpScope(this HttpContext context)
    {
        return context?.Items.TryGetValue(_scopeKey, out var scope) == true ? scope as McpScope : null;
    }

    public static void SetMcpScope(this HttpContext context, McpScope scope)
    {
        context.Items[_scopeKey] = scope;
    }

    // Path of the MCP endpoint the request came to: /{scope}/mcp, or /mcp on a host of the scope.
    // The OAuth resource of the request is the public origin followed by this path.
    public static string GetMcpEndpointPath(this HttpContext context)
    {
        return context?.Items.TryGetValue(_endpointPathKey, out var path) == true && path is string value
            ? value
            : context.GetMcpScope()?.Path;
    }

    public static void SetMcpEndpointPath(this HttpContext context, string endpointPath)
    {
        context.Items[_endpointPathKey] = endpointPath;
    }
}
