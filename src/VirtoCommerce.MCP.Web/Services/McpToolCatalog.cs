using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Services;

// The tools a scope serves: the registered tools of the scope, without the ones a module removed there
// (McpScopeBuilder.RemoveTool), and for a replaced name the last registration only (McpScopeBuilder.ReplaceTool).
// Modules register in dependency order, so a module's replacement comes after the tool of the module it builds on.
public class McpToolCatalog(IEnumerable<McpServerTool> tools, IEnumerable<McpToolOverride> overrides)
{
    private readonly McpServerTool[] _tools = tools.ToArray();
    private readonly McpToolOverride[] _overrides = overrides.ToArray();

    // Every registered tool, as the modules registered them
    public IReadOnlyList<McpServerTool> All => _tools;

    // The tools served in the scope, or at the endpoints without a scope when it is null; each still has to be visible
    // to the scope (McpScopeSessionConfigurator.IsVisible).
    public virtual IEnumerable<McpServerTool> GetTools(McpScope scope)
    {
        return scope == null ? _tools : _tools.Where(tool => IsServed(tool, scope.Name));
    }

    public virtual bool IsServed(McpServerTool tool, string scopeName)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var name = tool.ProtocolTool.Name;
        var applying = _overrides.Where(x => x.AppliesTo(scopeName, name)).ToList();

        if (applying.Any(x => x.Kind == McpToolOverrideKind.Remove))
        {
            return false;
        }

        return applying.Count == 0 ||
            ReferenceEquals(tool, _tools.LastOrDefault(x => x.IsInMcpScope(scopeName) && string.Equals(x.ProtocolTool.Name, name, StringComparison.Ordinal)));
    }

    // Names registered more than once in a scope with no replacement declared for them: only the first is served.
    public IList<(string ScopeName, string Name)> FindUnresolvedDuplicates()
    {
        return Filters.McpScopeSessionConfigurator.FindDuplicateNames(_tools)
            .Where(x => x.ScopeName == null || !_overrides.Any(o => o.Kind == McpToolOverrideKind.Replace && o.AppliesTo(x.ScopeName, x.Name)))
            .ToList();
    }
}
