using System;

namespace VirtoCommerce.MCP.Core.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class McpRequiresApprovalAttribute : Attribute
{
}
