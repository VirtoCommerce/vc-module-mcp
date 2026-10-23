using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

public class McpScope
{
    public string Name { get; set; }

    public string Title { get; set; }

    public string Instructions { get; set; }

    public bool RequireAuthenticatedUser { get; set; }

    public IList<string> Permissions { get; set; } = [];

    public string Path => $"/{Name}/mcp";

    public string ProtectedResourceMetadataPath => $"/.well-known/oauth-protected-resource/{Name}/mcp";
}
