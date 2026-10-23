using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using Permissions = VirtoCommerce.MCP.Core.ModuleConstants.Security.Permissions;

namespace VirtoCommerce.MCP.Web.Controllers.Api;

[Authorize]
[Route("api/mcp")]
public class McpController(
    IMcpPendingChangeSearchService pendingChangeSearchService,
    IMcpAuditEntrySearchService auditEntrySearchService)
    : Controller
{
    [HttpPost("pending-changes/search")]
    [Authorize(Permissions.Approve)]
    public async Task<ActionResult<McpPendingChangeSearchResult>> SearchPendingChanges([FromBody] McpPendingChangeSearchCriteria criteria)
    {
        return Ok(await pendingChangeSearchService.SearchAsync(criteria));
    }

    [HttpPost("audit/search")]
    [Authorize(Permissions.AuditRead)]
    public async Task<ActionResult<McpAuditEntrySearchResult>> SearchAuditEntries([FromBody] McpAuditEntrySearchCriteria criteria)
    {
        return Ok(await auditEntrySearchService.SearchAsync(criteria));
    }
}
