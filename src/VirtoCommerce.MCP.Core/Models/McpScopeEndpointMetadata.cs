namespace VirtoCommerce.MCP.Core.Models;

// Endpoint metadata of an MCP endpoint: the scope it serves and its path, /{scope}/mcp or /mcp on a host of the scope.
// Middleware in the platform pipeline tells MCP requests apart by it, after routing has matched the endpoint.
public sealed class McpScopeEndpointMetadata(McpScope scope, string endpointPath)
{
    public McpScope Scope { get; } = scope;

    public string EndpointPath { get; } = endpointPath;
}
