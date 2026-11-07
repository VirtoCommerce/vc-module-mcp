namespace VirtoCommerce.MCP.Core;

// The scopes of this module. A module that registers a scope of its own, such as UCP's "ucp", owns its name.
public static class McpScopes
{
    public const string Admin = "admin";

    public const string Vendor = "vendor";

    // In every registered scope, for infrastructure tools such as the approval tools. Never on endpoints without a scope.
    public const string Any = "*";
}
