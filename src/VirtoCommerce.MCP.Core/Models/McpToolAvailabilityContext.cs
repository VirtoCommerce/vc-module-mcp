using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace VirtoCommerce.MCP.Core.Models;

// The call a tool switch is decided for: the scope and the tool, and, when there is one, the user and the request. A job
// that runs later in the background has no request.
public sealed class McpToolAvailabilityContext(string scopeName, string toolName = null, ClaimsPrincipal user = null, HttpContext httpContext = null)
{
    public string ScopeName { get; } = scopeName;

    public string ToolName { get; } = toolName;

    public ClaimsPrincipal User { get; } = user;

    public HttpContext HttpContext { get; } = httpContext;
}
