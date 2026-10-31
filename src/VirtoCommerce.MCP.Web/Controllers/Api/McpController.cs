using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.MCP.Web.Services;
using Permissions = VirtoCommerce.MCP.Core.ModuleConstants.Security.Permissions;

namespace VirtoCommerce.MCP.Web.Controllers.Api;

// The back office of the MCP infrastructure: the approval queue, the audit log and the tool switches.
[Authorize]
[Route("api/mcp")]
public class McpController(
    IMcpPendingChangeSearchService pendingChangeSearchService,
    IMcpAuditEntrySearchService auditEntrySearchService,
    IMcpApprovalService approvalService,
    IMcpScopeRegistry scopeRegistry,
    McpPendingChangeReviewService reviewService,
    McpToolAuthorizationService toolAuthorizationService,
    McpToolAvailabilityService toolAvailabilityService,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider)
    : Controller
{
    /// <summary>
    /// MCP scopes, with whether the current user reviews the changes of each.
    /// </summary>
    [HttpGet("scopes")]
    public async Task<ActionResult<McpScopeInfo[]>> GetScopes()
    {
        var result = new List<McpScopeInfo>();

        foreach (var scope in scopeRegistry.Scopes.OrderBy(x => x.Name))
        {
            var disabledToolNames = await toolAvailabilityService.GetDisabledToolNamesAsync(scope.Name);

            result.Add(new McpScopeInfo
            {
                Name = scope.Name,
                Title = scope.Title ?? scope.Name,
                Path = scope.Path,
                SupportsApproval = scope.SupportsApproval,
                ApprovalPermission = scope.ApprovalPermission,
                IsApprover = await toolAuthorizationService.IsApproverAsync(User, scope),
                IsDisabled = disabledToolNames.Contains(McpToolAvailabilityService.AllTools),
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// What needs attention: changes waiting for the user's review, failed tool calls of the last day and switched-off tools.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<McpBackOfficeSummary>> GetSummary()
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
        var summary = new McpBackOfficeSummary { Since = since };

        var reviewScopes = await reviewService.GetReviewScopesAsync(User);
        if (reviewScopes.Count > 0)
        {
            var pending = await pendingChangeSearchService.SearchAsync(new McpPendingChangeSearchCriteria
            {
                ScopeNames = reviewScopes.Select(x => x.Name).ToList(),
                Statuses = [McpPendingChangeStatus.Pending],
                Take = 0,
            });
            summary.PendingChangeCount = pending.TotalCount;
        }

        if (await IsAuthorizedAsync(Permissions.AuditRead))
        {
            var failed = await auditEntrySearchService.SearchAsync(new McpAuditEntrySearchCriteria
            {
                Outcomes = [McpAuditOutcome.Failed, McpAuditOutcome.Error],
                StartDate = since,
                Take = 0,
            });
            summary.FailedCallCount = failed.TotalCount;
        }

        if (await IsAuthorizedAsync(Permissions.ToolsManage))
        {
            summary.DisabledToolCount = (await GetToolInfosAsync()).Count(x => !x.IsEnabled);
        }

        return Ok(summary);
    }

    /// <summary>
    /// Search changes that MCP tools submitted for approval, in the scopes where the current user is an approver.
    /// </summary>
    [HttpPost("pending-changes/search")]
    public async Task<ActionResult<McpPendingChangeSearchResult>> SearchPendingChanges([FromBody] McpPendingChangeSearchCriteria criteria)
    {
        criteria ??= new McpPendingChangeSearchCriteria();

        var reviewScopeNames = (await reviewService.GetReviewScopesAsync(User)).Select(x => x.Name).ToList();
        var requested = new List<string>(criteria.ScopeNames ?? []);
        if (!string.IsNullOrEmpty(criteria.ScopeName))
        {
            requested.Add(criteria.ScopeName);
        }

        var scopeNames = requested.Count == 0
            ? reviewScopeNames
            : reviewScopeNames.Where(x => requested.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();

        if (scopeNames.Count == 0)
        {
            return Forbid();
        }

        criteria.ScopeName = null;
        criteria.ScopeNames = scopeNames;

        var result = await pendingChangeSearchService.SearchAsync(criteria);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var visible = new List<McpPendingChange>();

        foreach (var change in result.Results)
        {
            if (await approvalService.CanReviewAsync(User, change))
            {
                change.Arguments = change.GetMaskedArguments();
                change.Status = change.IsExpired(now) ? McpPendingChangeStatus.Expired : change.Status;
                visible.Add(change);
            }
        }

        result.Results = visible;

        return Ok(result);
    }

    /// <summary>
    /// Get a change submitted for approval with its arguments, dry-run preview and result.
    /// </summary>
    [HttpGet("pending-changes/{id}")]
    public Task<ActionResult<McpPendingChangeDetails>> GetPendingChange([FromRoute] string id)
    {
        return ReviewAsync(() => reviewService.GetDetailsAsync(User, id));
    }

    /// <summary>
    /// Approve a pending change and execute it right away, as the current user.
    /// </summary>
    [HttpPost("pending-changes/{id}/approve")]
    public Task<ActionResult<McpPendingChangeDetails>> ApprovePendingChange([FromRoute] string id, [FromBody] McpReviewRequest request)
    {
        return ReviewAsync(() => reviewService.ApproveAsync(User, id, request?.Comment, HttpContext.RequestServices));
    }

    /// <summary>
    /// Reject a pending change so that it is never executed.
    /// </summary>
    [HttpPost("pending-changes/{id}/reject")]
    public Task<ActionResult<McpPendingChangeDetails>> RejectPendingChange([FromRoute] string id, [FromBody] McpReviewRequest request)
    {
        return ReviewAsync(() => reviewService.RejectAsync(User, id, request?.Comment));
    }

    /// <summary>
    /// Search the audit log of MCP tool calls.
    /// </summary>
    [HttpPost("audit/search")]
    [Authorize(Permissions.AuditRead)]
    public async Task<ActionResult<McpAuditEntrySearchResult>> SearchAuditEntries([FromBody] McpAuditEntrySearchCriteria criteria)
    {
        return Ok(await auditEntrySearchService.SearchAsync(criteria ?? new McpAuditEntrySearchCriteria()));
    }

    /// <summary>
    /// The tools of every scope, with whether agents can use them now.
    /// </summary>
    [HttpGet("tools")]
    [Authorize(Permissions.ToolsManage)]
    public async Task<ActionResult<McpToolInfo[]>> GetTools()
    {
        return Ok(await GetToolInfosAsync());
    }

    /// <summary>
    /// Switch a tool, or with * every tool of a scope, on or off. Applies to the next request, with no restart.
    /// </summary>
    [HttpPut("tools/availability")]
    [Authorize(Permissions.ToolsManage)]
    public async Task<ActionResult<McpToolInfo[]>> SetToolAvailability([FromBody] McpToolAvailabilityRequest request)
    {
        if (string.IsNullOrEmpty(request?.ScopeName) || string.IsNullOrEmpty(request.ToolName) || scopeRegistry.GetScope(request.ScopeName) == null)
        {
            return BadRequest(new { message = "A known scope name and a tool name, or *, are required." });
        }

        await toolAvailabilityService.SetEnabledAsync(request.ScopeName, request.ToolName, request.Enabled, scopeRegistry.Scopes.Select(x => x.Name));

        return Ok(await GetToolInfosAsync());
    }

    private async Task<IList<McpToolInfo>> GetToolInfosAsync()
    {
        var tools = HttpContext.RequestServices.GetServices<McpServerTool>().ToList();
        var result = new List<McpToolInfo>();

        foreach (var scope in scopeRegistry.Scopes.OrderBy(x => x.Name))
        {
            var disabledToolNames = await toolAvailabilityService.GetDisabledToolNamesAsync(scope.Name);
            var scopeDisabled = disabledToolNames.Contains(McpToolAvailabilityService.AllTools);

            result.AddRange(tools
                .Where(tool => McpScopeSessionConfigurator.IsVisible(tool, scope))
                .OrderBy(tool => tool.ProtocolTool.Name)
                .Select(tool =>
                {
                    var toolDisabled = disabledToolNames.Contains(tool.ProtocolTool.Name);

                    return new McpToolInfo
                    {
                        ScopeName = scope.Name,
                        Name = tool.ProtocolTool.Name,
                        Title = tool.ProtocolTool.Title ?? tool.ProtocolTool.Annotations?.Title,
                        Description = tool.ProtocolTool.Description,
                        ReadOnly = tool.ProtocolTool.Annotations?.ReadOnlyHint == true,
                        Destructive = tool.ProtocolTool.Annotations?.DestructiveHint == true,
                        RequiresApproval = tool.Metadata.OfType<McpRequiresApprovalAttribute>().Any(),
                        IsEnabled = !scopeDisabled && !toolDisabled,
                        DisabledBy = GetDisabledBy(scopeDisabled, toolDisabled),
                    };
                }));
        }

        return result;
    }

    private static string GetDisabledBy(bool scopeDisabled, bool toolDisabled)
    {
        if (scopeDisabled)
        {
            return McpToolDisabledBy.Scope;
        }

        return toolDisabled ? McpToolDisabledBy.Tool : null;
    }

    private async Task<bool> IsAuthorizedAsync(string permission)
    {
        return (await authorizationService.AuthorizeAsync(User, permission)).Succeeded;
    }

    private async Task<ActionResult<McpPendingChangeDetails>> ReviewAsync(Func<Task<McpPendingChangeDetails>> review)
    {
        try
        {
            return Ok(await review());
        }
        catch (McpForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (McpException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
