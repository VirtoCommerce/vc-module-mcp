using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

// Decides whether a browser page of an origin may call the MCP endpoint of a scope. The MCP transport requires the server
// to validate the Origin header of every request, so a page a user opens cannot reach an MCP server through DNS
// rebinding; a request with an origin that is not allowed is answered with 403.
public interface IMcpOriginValidator
{
    // Called for a request that carries an Origin header; a request without one is not checked.
    Task<bool> IsAllowedAsync(HttpContext context, McpScope scope, string origin);
}
