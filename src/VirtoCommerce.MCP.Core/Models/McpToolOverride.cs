using System;

namespace VirtoCommerce.MCP.Core.Models;

// What a module declared about another module's tool in one scope (McpScopeBuilder.RemoveTool / ReplaceTool): the tool
// is not served there, or the last registration of its name is served instead of the first.
public sealed class McpToolOverride(string scopeName, string toolName, McpToolOverrideKind kind)
{
    public string ScopeName { get; } = scopeName;

    public string ToolName { get; } = toolName;

    public McpToolOverrideKind Kind { get; } = kind;

    public bool AppliesTo(string scopeName, string toolName)
    {
        return string.Equals(ScopeName, scopeName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ToolName, toolName, StringComparison.Ordinal);
    }
}

public enum McpToolOverrideKind
{
    Remove,
    Replace,
}
