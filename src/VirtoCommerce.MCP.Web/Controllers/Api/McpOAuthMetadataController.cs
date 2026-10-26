using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.MCP.Core;
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
    public ActionResult<McpProtectedResourceMetadata> GetProtectedResourceMetadata([FromRoute] string scopeName)
    {
        var scope = scopeRegistry.GetScope(scopeName);
        if (scope == null)
        {
            return NotFound();
        }

        var origin = originResolver.GetOrigin(HttpContext, scope);

        return Ok(new McpProtectedResourceMetadata
        {
            Resource = origin + scope.Path,
            AuthorizationServers = [origin + "/"],
            ScopesSupported = [.. ModuleConstants.Security.OAuthScopes],
            BearerMethodsSupported = ["header"],
            ResourceName = scope.Title ?? scope.Name,
        });
    }
}
