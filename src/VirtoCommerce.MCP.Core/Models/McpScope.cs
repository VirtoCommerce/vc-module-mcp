using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

public class McpScope
{
    public const string HostEndpointPath = "/mcp";

    public string Name { get; set; }

    public string Title { get; set; }

    // Version reported to MCP clients in the server info. Defaults to the version of the MCP module.
    public string Version { get; set; }

    public string Instructions { get; set; }

    public bool RequireAuthenticatedUser { get; set; }

    public IList<string> Permissions { get; set; } = [];

    // The permission of the users who review pending changes in this scope, for example mcp:approve. Without it, tools
    // that need approval are refused in the scope, and the approval tools are not there.
    public string ApprovalPermission { get; set; }

    public bool SupportsApproval => !string.IsNullOrEmpty(ApprovalPermission);

    // Agents that register themselves may connect with the scope's URL only; otherwise only the agents an administrator
    // registered can. Off unless the module that registers the scope turns it on; MCP:Scopes:{scope}:AllowSelfRegisteredClients
    // overrides it.
    public bool AllowSelfRegisteredClients { get; set; }

    public string Path => $"/{Name}/mcp";

    public string ProtectedResourceMetadataPath => GetProtectedResourceMetadataPath(Path);

    public static string GetProtectedResourceMetadataPath(string endpointPath)
    {
        return "/.well-known/oauth-protected-resource" + endpointPath;
    }
}
