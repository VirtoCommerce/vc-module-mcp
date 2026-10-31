using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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
        var scopes = primitive.GetMcpScopes();
        return scopes.Contains(scopeName, StringComparer.OrdinalIgnoreCase) || scopes.Contains(McpScopes.Any);
    }

    public static IReadOnlyList<string> GetSensitiveArgumentPaths(this IMcpServerPrimitive primitive)
    {
        return primitive.Metadata.OfType<MethodInfo>().FirstOrDefault() is { } method
            ? McpSensitiveArguments.GetPaths(method)
            : [];
    }

    public static bool RequiresApproval(this IMcpServerPrimitive primitive)
    {
        return primitive.Metadata.OfType<McpRequiresApprovalAttribute>().Any();
    }

    // Whether this call needs approval: always, or as the Condition of [McpRequiresApproval] decides from the arguments.
    // A condition that cannot be found counts as "needs approval", so a typo never skips the approval.
    public static bool RequiresApproval(this IMcpServerPrimitive primitive, IDictionary<string, JsonElement> arguments)
    {
        var attribute = primitive.Metadata.OfType<McpRequiresApprovalAttribute>().FirstOrDefault();
        if (attribute == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(attribute.Condition))
        {
            return true;
        }

        var condition = primitive.FindApprovalCondition();
        IReadOnlyDictionary<string, JsonElement> values = arguments as IReadOnlyDictionary<string, JsonElement> ?? new Dictionary<string, JsonElement>(arguments ?? new Dictionary<string, JsonElement>());

        return condition == null || (bool)condition.Invoke(null, [values])!;
    }

    // The public static bool method named by the Condition of [McpRequiresApproval], taking the arguments.
    public static MethodInfo FindApprovalCondition(this IMcpServerPrimitive primitive)
    {
        var condition = primitive.Metadata.OfType<McpRequiresApprovalAttribute>().FirstOrDefault()?.Condition;
        var declaringType = primitive.Metadata.OfType<MethodInfo>().FirstOrDefault()?.DeclaringType;
        if (string.IsNullOrEmpty(condition) || declaringType == null)
        {
            return null;
        }

        var method = declaringType.GetMethod(condition, BindingFlags.Public | BindingFlags.Static, [typeof(IReadOnlyDictionary<string, JsonElement>)]);

        return method?.ReturnType == typeof(bool) ? method : null;
    }
}
