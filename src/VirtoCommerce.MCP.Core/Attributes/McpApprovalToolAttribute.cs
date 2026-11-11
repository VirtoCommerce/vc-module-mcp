using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// A primitive that exists only in scopes with approval (McpScope.ApprovalPermission set), such as the tools that
// review pending changes. Combine it with [McpScope(McpScopes.Any)].
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class McpApprovalToolAttribute : Attribute
{
}

// A tool only the approvers of the scope may see and call: users with the scope's ApprovalPermission.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class McpApproverOnlyAttribute : Attribute
{
}
