using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpScopeRegistry
{
    IReadOnlyCollection<McpScope> Scopes { get; }

    McpScope GetScope(string name);

    McpScope FindScopeByPath(PathString path);

    // The scope served at /mcp on the host, configured with MCP:Scopes:{scope}:Hosts.
    McpScope FindScopeByHost(HostString host);

    // The scope of an MCP endpoint: /{scope}/mcp on any host, or /mcp on a host of the scope.
    McpScope FindScope(HostString host, PathString path);
}
