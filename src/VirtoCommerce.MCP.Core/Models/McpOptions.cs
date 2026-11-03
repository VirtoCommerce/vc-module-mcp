using System;
using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

public class McpOptions
{
    public const long DefaultMaxRequestBodySize = 1_048_576;

    public string PublicOrigin { get; set; }

    // The largest MCP request body in bytes; a larger request is answered with 413 before it is read. Tool arguments are
    // small JSON, while the platform itself sets no limit.
    public long MaxRequestBodySize { get; set; } = DefaultMaxRequestBodySize;

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
