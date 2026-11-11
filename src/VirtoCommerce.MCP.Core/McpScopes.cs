namespace VirtoCommerce.MCP.Core;

// The scopes of this module. A module that registers a scope of its own, such as UCP's "ucp", owns its name.
public static class McpScopes
{
    public const string Admin = "admin";

    // Registered by the vendor portal module, which owns its vendor_portal:access permission; this module only puts its job
    // tools in it.
    public const string Vendor = "vendor";

    // In every registered scope, for infrastructure tools such as the approval tools. Never on endpoints without a scope.
    public const string Any = "*";
}
