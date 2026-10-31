using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Security;
using Permissions = VirtoCommerce.MCP.Core.ModuleConstants.Security.Permissions;

namespace VirtoCommerce.MCP.Web.Controllers.Api;

// The AI agents users connected to MCP endpoints. A user sees and disconnects their own agents; a user with
// mcp:agents:manage sees and disconnects everyone's.
[Authorize]
[Route("api/mcp/connected-agents")]
public class McpConnectedAgentsController(
    McpConnectedAgentService connectedAgentService,
    IAuthorizationService authorizationService)
    : Controller
{
    /// <summary>
    /// Search connected agents: the current user's, or everyone's with mcp:agents:manage.
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<McpConnectedAgentSearchResult>> Search([FromBody] McpConnectedAgentSearchCriteria criteria)
    {
        criteria ??= new McpConnectedAgentSearchCriteria();

        if (!await CanManageAsync())
        {
            criteria.UserId = User.GetUserId();
        }

        return Ok(await connectedAgentService.SearchAsync(criteria));
    }

    /// <summary>
    /// Disconnect an agent: its authorization and tokens are revoked, and its next request is refused.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Revoke([FromRoute] string id)
    {
        var result = await connectedAgentService.RevokeAsync(User, id, await CanManageAsync());

        return result switch
        {
            McpConnectedAgentRevokeResult.Revoked => NoContent(),
            McpConnectedAgentRevokeResult.Forbidden => Forbid(),
            _ => NotFound(),
        };
    }

    private async Task<bool> CanManageAsync()
    {
        return (await authorizationService.AuthorizeAsync(User, Permissions.AgentsManage)).Succeeded;
    }
}
