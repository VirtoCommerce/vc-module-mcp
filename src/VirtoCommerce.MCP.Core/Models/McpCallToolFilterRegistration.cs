using System;

namespace VirtoCommerce.MCP.Core.Models;

// A module's call filter of one scope (McpScopes.Any for every scope), as AddMcpCallToolFilter registered it, so the
// MCP module also runs it where the MCP server does not call the tool: the execution of an approved change.
public sealed class McpCallToolFilterRegistration(string scopeName, Type filterType)
{
    public string ScopeName { get; } = scopeName;

    public Type FilterType { get; } = filterType;

    public bool AppliesTo(string scopeName)
    {
        return ScopeName == McpScopes.Any || string.Equals(ScopeName, scopeName, StringComparison.OrdinalIgnoreCase);
    }
}
