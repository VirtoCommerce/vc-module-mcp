namespace VirtoCommerce.MCP.Core;

public static class McpScopes
{
    // Registered by the UCP module when it moves to scopes. Until then UCP maps /ucp/mcp itself,
    // and another registration of this name would take that path over.
    public const string Ucp = "ucp";

    public const string Admin = "admin";

    public const string Vendor = "vendor";
}
