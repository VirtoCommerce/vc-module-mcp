using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpHttpContextExtensions
{
    private static readonly object _scopeKey = new();
    private static readonly object _endpointPathKey = new();
    private static readonly object _requestKey = new();

    // The scope of the request: the one set for it, or the one of the MCP endpoint that routing matched.
    public static McpScope GetMcpScope(this HttpContext context)
    {
        if (context == null)
        {
            return null;
        }

        return context.Items.TryGetValue(_scopeKey, out var scope) && scope is McpScope value
            ? value
            : context.GetMcpScopeEndpoint()?.Scope;
    }

    public static void SetMcpScope(this HttpContext context, McpScope scope)
    {
        context.Items[_scopeKey] = scope;
    }

    // Path of the MCP endpoint the request came to: /{scope}/mcp, or /mcp on a host of the scope.
    // The OAuth resource of the request is the public origin followed by this path.
    public static string GetMcpEndpointPath(this HttpContext context)
    {
        if (context == null)
        {
            return null;
        }

        if (context.Items.TryGetValue(_endpointPathKey, out var path) && path is string value)
        {
            return value;
        }

        return context.GetMcpScopeEndpoint()?.EndpointPath ?? context.GetMcpScope()?.Path;
    }

    public static void SetMcpEndpointPath(this HttpContext context, string endpointPath)
    {
        context.Items[_endpointPathKey] = endpointPath;
    }

    // The JSON-RPC request of an MCP endpoint, read once after routing (UseMcpScopeRequests), so middleware of the
    // platform pipeline sees the method and the tool without parsing the body; null when it is not an MCP request.
    public static McpRequestInfo GetMcpRequest(this HttpContext context)
    {
        return context?.Items.TryGetValue(_requestKey, out var request) == true ? request as McpRequestInfo : null;
    }

    public static void SetMcpRequest(this HttpContext context, McpRequestInfo request)
    {
        context.Items[_requestKey] = request;
    }

    // The MCP endpoint that routing matched for the request, or null when it is not an MCP request.
    public static McpScopeEndpointMetadata GetMcpScopeEndpoint(this HttpContext context)
    {
        return context?.GetEndpoint()?.Metadata.GetMetadata<McpScopeEndpointMetadata>();
    }
}
