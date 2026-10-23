using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpServerPrimitiveExtensions
{
    public static IList<string> GetMcpScopes(this IMcpServerPrimitive primitive)
    {
        return primitive.Metadata
            .OfType<McpScopeAttribute>()
            .SelectMany(x => x.Scopes)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsInMcpScope(this IMcpServerPrimitive primitive, string scopeName)
    {
        return primitive.GetMcpScopes().Contains(scopeName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool RequiresApproval(this IMcpServerPrimitive primitive)
    {
        return primitive.Metadata.OfType<McpRequiresApprovalAttribute>().Any();
    }
}
