using System;

namespace VirtoCommerce.MCP.Core.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpScopeAttribute : Attribute
{
    public McpScopeAttribute(params string[] scopes)
    {
        Scopes = scopes ?? [];
    }

    public string[] Scopes { get; }
}
