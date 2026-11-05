using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace VirtoCommerce.MCP.Core.Models;

// The call a tool switch is decided for: the scope and the tool, and, when there is one, the user and the request. The
// user is the one the call runs for: the caller of a tool; at the approval of a pending change, the switch is decided
// for the approver and then for the requester, with the approver's request; for a background job, for the user it runs
// as and for the requester of the change that started it, without a request.
public sealed class McpToolAvailabilityContext(string scopeName, string toolName = null, ClaimsPrincipal user = null, HttpContext httpContext = null)
{
    public string ScopeName { get; } = scopeName;

    public string ToolName { get; } = toolName;

    public ClaimsPrincipal User { get; } = user;

    public HttpContext HttpContext { get; } = httpContext;
}
