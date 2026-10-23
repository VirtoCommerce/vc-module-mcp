using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Data.Services;

public class McpScopeRegistry(
    IEnumerable<McpScope> scopes,
    IOptions<McpOptions> options)
    : IMcpScopeRegistry
{
    private const string McpPathSegment = "mcp";

    private readonly Dictionary<string, McpScope> _scopes = GetEnabledScopes(scopes, options.Value);

    public IReadOnlyCollection<McpScope> Scopes => _scopes.Values;

    public McpScope GetScope(string name)
    {
        return name != null && _scopes.TryGetValue(name, out var scope) ? scope : null;
    }

    public McpScope FindScopeByPath(PathString path)
    {
        return path.Value?.Trim('/').Split('/') is [var scopeName, var lastSegment] &&
            string.Equals(lastSegment, McpPathSegment, StringComparison.OrdinalIgnoreCase)
            ? GetScope(scopeName)
            : null;
    }

    private static Dictionary<string, McpScope> GetEnabledScopes(IEnumerable<McpScope> scopes, McpOptions options)
    {
        return scopes
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .Where(x => !options.Scopes.TryGetValue(x.Name, out var scopeOptions) || scopeOptions.Enabled)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }
}
