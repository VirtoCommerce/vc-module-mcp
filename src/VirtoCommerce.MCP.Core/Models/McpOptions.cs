using System;
using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

public class McpOptions
{
    public string PublicOrigin { get; set; }

    public Dictionary<string, McpScopeOptions> Scopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class McpScopeOptions
{
    public bool Enabled { get; set; } = true;

    public string PublicOrigin { get; set; }

    // Hosts that serve the scope at /mcp, for example a storefront host for a buyer scope. An entry with a port matches that port only.
    // The scope stays available at /{scope}/mcp on every host.
    public IList<string> Hosts { get; set; } = [];
}
