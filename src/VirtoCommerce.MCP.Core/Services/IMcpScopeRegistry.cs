using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpScopeRegistry
{
    IReadOnlyCollection<McpScope> Scopes { get; }

    McpScope GetScope(string name);

    McpScope FindScopeByPath(PathString path);
}
