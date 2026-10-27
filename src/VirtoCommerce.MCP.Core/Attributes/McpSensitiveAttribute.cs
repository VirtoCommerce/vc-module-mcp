using System;

namespace VirtoCommerce.MCP.Core.Attributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class McpSensitiveAttribute : Attribute
{
}
