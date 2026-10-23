using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpHttpContextExtensions
{
    private static readonly object _scopeKey = new();

    public static McpScope GetMcpScope(this HttpContext context)
    {
        return context?.Items.TryGetValue(_scopeKey, out var scope) == true ? scope as McpScope : null;
    }

    public static void SetMcpScope(this HttpContext context, McpScope scope)
    {
        context.Items[_scopeKey] = scope;
    }
}
