using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// Requires platform permissions, granted to the user either globally or limited by permission scopes, for example
// marketing:read for some stores only. [Authorize] accepts global permissions only, so a user whose permission is
// limited to a store would not see the tool at all. With this attribute the tool is visible to that user, and the tool
// limits the data itself with McpPermissionGrant (ClaimsPrincipal.GetMcpPermissionGrant) and McpForbiddenException.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpPermissionAttribute : Attribute
{
    public McpPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions ?? [];
    }

    public string[] Permissions { get; }
}
