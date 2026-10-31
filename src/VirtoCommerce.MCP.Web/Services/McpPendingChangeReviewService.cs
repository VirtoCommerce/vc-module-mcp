using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Diagnostics;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Services;

// Reviews pending changes in the back office: the same rules as the approval tools of an MCP scope, for the approvers of
// the change's scope. An approved change runs right away as the approving user.
public class McpPendingChangeReviewService(
    IMcpPendingChangeService pendingChangeService,
    IMcpApprovalService approvalService,
    IMcpScopeRegistry scopeRegistry,
    McpToolAuthorizationService toolAuthorizationService,
    McpToolAvailabilityService toolAvailabilityService,
    McpPendingChangeExecutor pendingChangeExecutor,
    TimeProvider timeProvider)
{
    // Client id of the audit entries of reviews made in the back office rather than by an agent.
    public const string BackOfficeClientId = "back-office";

    public virtual async Task<IList<McpScope>> GetReviewScopesAsync(ClaimsPrincipal user)
    {
        var scopes = new List<McpScope>();

        foreach (var scope in scopeRegistry.Scopes.Where(x => x.SupportsApproval))
        {
            if (await toolAuthorizationService.IsApproverAsync(user, scope))
            {
                scopes.Add(scope);
            }
        }

        return scopes;
    }

    public virtual async Task<McpPendingChangeDetails> GetDetailsAsync(ClaimsPrincipal user, string changeId)
    {
        var change = await GetReviewableChangeAsync(user, changeId);

        return await ToDetailsAsync(user, change);
    }

    public virtual async Task<McpPendingChangeDetails> ApproveAsync(ClaimsPrincipal user, string changeId, string comment, IServiceProvider services)
    {
        var change = await GetReviewableChangeAsync(user, changeId);
        var scope = scopeRegistry.GetScope(change.ScopeName);
        var tool = await FindAvailableToolAsync(services, scope, change.ToolName)
            ?? throw new McpException($"Tool '{change.ToolName}' of pending change '{changeId}' is not available now: it was removed or disabled by the administrator.");

        if (!await toolAuthorizationService.IsAuthorizedAsync(user, tool))
        {
            throw new McpForbiddenException($"You are not allowed to run tool '{change.ToolName}', so you cannot approve its changes.");
        }

        using var activity = McpDiagnostics.StartReview(scope.Name, change.ToolName, "approve", change.Id, BackOfficeClientId);

        change = await approvalService.ApproveAsync(change.Id, user.GetUserId(), user.GetUserName(), comment);

        var result = await pendingChangeExecutor.ExecuteAsync(services, user, scope, tool, change, BackOfficeClientId);
        var succeeded = result.IsError != true;
        change = await approvalService.CompleteAsync(change, JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions), succeeded);

        await pendingChangeExecutor.WriteReviewAuditAsync(user, change, ModuleConstants.ToolNames.ApprovePendingChange, BackOfficeClientId);

        return await ToDetailsAsync(user, change);
    }

    public virtual async Task<McpPendingChangeDetails> RejectAsync(ClaimsPrincipal user, string changeId, string reason)
    {
        var change = await GetReviewableChangeAsync(user, changeId);

        using var activity = McpDiagnostics.StartReview(change.ScopeName, change.ToolName, "reject", change.Id, BackOfficeClientId);

        change = await approvalService.RejectAsync(change.Id, user.GetUserId(), user.GetUserName(), reason);
        await pendingChangeExecutor.WriteReviewAuditAsync(user, change, ModuleConstants.ToolNames.RejectPendingChange, BackOfficeClientId);

        return await ToDetailsAsync(user, change);
    }

    // The tool as the scope serves it now: in the scope, and not switched off.
    public virtual async Task<McpServerTool> FindAvailableToolAsync(IServiceProvider services, McpScope scope, string toolName)
    {
        if (scope == null || await toolAvailabilityService.IsDisabledAsync(scope.Name, toolName))
        {
            return null;
        }

        return services.GetServices<McpServerTool>()
            .FirstOrDefault(x => x.ProtocolTool.Name == toolName && x.IsInMcpScope(scope.Name));
    }

    // A change the user may review: one of a scope they approve in, that the scope's rules let them see.
    protected virtual async Task<McpPendingChange> GetReviewableChangeAsync(ClaimsPrincipal user, string changeId)
    {
        var change = string.IsNullOrEmpty(changeId) ? null : await pendingChangeService.GetByIdAsync(changeId);
        var scope = change == null ? null : scopeRegistry.GetScope(change.ScopeName);

        if (scope == null || !await toolAuthorizationService.IsApproverAsync(user, scope) || !await approvalService.CanReviewAsync(user, change))
        {
            throw new McpForbiddenException($"Pending change '{changeId}' was not found, or you may not review it.");
        }

        return change;
    }

    protected virtual async Task<McpPendingChangeDetails> ToDetailsAsync(ClaimsPrincipal user, McpPendingChange change)
    {
        var status = change.IsExpired(timeProvider.GetUtcNow().UtcDateTime) ? McpPendingChangeStatus.Expired : change.Status;
        var masked = (McpPendingChange)change.Clone();
        masked.Arguments = change.GetMaskedArguments();

        return new McpPendingChangeDetails
        {
            Change = masked,
            Status = status,
            ArgumentsJson = McpToolResultJson.ToIndentedString(McpToolResultJson.Parse(masked.Arguments)),
            PreviewJson = McpToolResultJson.ToIndentedString(McpToolResultJson.Unwrap(change.Preview)),
            ResultJson = McpToolResultJson.ToIndentedString(McpToolResultJson.Unwrap(change.Result)),
            CanReview = status == McpPendingChangeStatus.Pending,
            CannotApproveReason = await GetCannotApproveReasonAsync(user, change, status),
        };
    }

    private async Task<string> GetCannotApproveReasonAsync(ClaimsPrincipal user, McpPendingChange change, string status)
    {
        if (status != McpPendingChangeStatus.Pending)
        {
            return $"The change is {status.ToLowerInvariant()}.";
        }

        var isRequester = !string.IsNullOrEmpty(change.RequestedByUserId) &&
            string.Equals(change.RequestedByUserId, user.GetUserId(), StringComparison.OrdinalIgnoreCase);

        if (isRequester && !await approvalService.IsSelfApprovalAllowedAsync())
        {
            return "You requested this change, so another user must approve it.";
        }

        return null;
    }
}
