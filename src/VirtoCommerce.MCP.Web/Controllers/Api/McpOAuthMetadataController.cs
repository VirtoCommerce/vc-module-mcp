using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Models;

namespace VirtoCommerce.MCP.Web.Controllers.Api;

[ApiController]
[AllowAnonymous]
public class McpOAuthMetadataController(
    IMcpScopeRegistry scopeRegistry,
    IMcpPublicOriginResolver originResolver)
    : ControllerBase
{
    /// <summary>
    /// OAuth protected resource metadata (RFC 9728) of an MCP scope: the resource to request a token for and the authorization server.
    /// </summary>
    [HttpGet("~/.well-known/oauth-protected-resource/{scopeName}/mcp")]
    [ProducesResponseType(typeof(McpProtectedResourceMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<McpProtectedResourceMetadata>> GetProtectedResourceMetadata([FromRoute] string scopeName)
    {
        var scope = scopeRegistry.GetScope(scopeName);

        return GetMetadataAsync(scope, scope?.Path);
    }

    /// <summary>
    /// OAuth protected resource metadata (RFC 9728) of the MCP scope served at /mcp on the request host (MCP:Scopes:{scope}:Hosts).
    /// </summary>
    [HttpGet("~/.well-known/oauth-protected-resource/mcp")]
    [ProducesResponseType(typeof(McpProtectedResourceMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<McpProtectedResourceMetadata>> GetHostProtectedResourceMetadata()
    {
        var scope = scopeRegistry.FindScopeByHost(Request.Host);
        HttpContext.SetMcpScope(scope);
        HttpContext.SetMcpEndpointPath(McpScope.HostEndpointPath);

        return GetMetadataAsync(scope, McpScope.HostEndpointPath);
    }

    private async Task<ActionResult<McpProtectedResourceMetadata>> GetMetadataAsync(McpScope scope, string endpointPath)
    {
        if (scope == null)
        {
            return NotFound();
        }

        var origin = await originResolver.GetOriginAsync(HttpContext, scope);

        return Ok(new McpProtectedResourceMetadata
        {
            Resource = origin + endpointPath,
            AuthorizationServers = [origin + "/"],
            ScopesSupported = [.. ModuleConstants.Security.OAuthScopes],
            BearerMethodsSupported = ["header"],
            ResourceName = scope.Title ?? scope.Name,
        });
    }
}
