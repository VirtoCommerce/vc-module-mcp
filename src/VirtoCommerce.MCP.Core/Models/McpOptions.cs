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
}
