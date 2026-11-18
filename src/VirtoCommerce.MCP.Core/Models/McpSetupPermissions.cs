namespace VirtoCommerce.MCP.Core.Models;

// The existing permissions that guard a part of a setup.
public class McpSetupPermissions
{
    public string Read { get; set; }

    public string Update { get; set; }

    // Sets the part up on an object that is being created.
    public string Create { get; set; }

    // The permission scope that limits Read and Update to some objects, such as StoreSelectedScope; null when the
    // module's permissions cannot be limited.
    public string ScopeType { get; set; }
}
