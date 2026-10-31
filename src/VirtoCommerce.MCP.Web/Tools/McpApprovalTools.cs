using System;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Tools;

// The review tools of every scope with approval (McpScope.ApprovalPermission). They work with the changes of the
// scope of the request only, and the approvers are the users with the scope's approval permission.
[McpServerToolType]
[McpScope(McpScopes.Any)]
[McpApprovalTool]
public class McpApprovalTools(
    IMcpPendingChangeService pendingChangeService,
    IMcpPendingChangeSearchService pendingChangeSearchService,
    IMcpApprovalService approvalService,
    McpToolAuthorizationService toolAuthorizationService,
    McpPendingChangeExecutor pendingChangeExecutor,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    [McpServerTool(Name = ModuleConstants.ToolNames.ListPendingChanges, ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("List changes that agents submitted for approval, newest first. An approver sees every change, " +
        "any other user only the changes they requested. Returns compact rows; " +
        "call get_pending_change for the arguments and the dry-run preview of one change.")]
    public async Task<object> ListPendingChanges(
        RequestContext<CallToolRequestParams> requestContext,
        IMcpPageSizeResolver pageSizeResolver,
        [Description("Status filter: Pending (default), Approved, Applied, Failed, Rejected or Expired.")] string status = McpPendingChangeStatus.Pending,
        [Description("Only changes submitted by this tool.")] string tool_name = null,
        [Description("Number of rows to skip.")] int skip = 0,
        [Description("Number of rows to return. Omit it for the default page size; larger values are capped.")] int? take = null)
    {
        var criteria = new McpPendingChangeSearchCriteria
        {
            ScopeName = Scope.Name,
            ToolName = tool_name,
            Statuses = string.IsNullOrEmpty(status) ? null : [status],
            Skip = Math.Max(skip, 0),
            Take = await pageSizeResolver.GetPageSizeAsync(take),
        };

        if (!await IsApproverAsync(requestContext.User))
        {
            var userId = requestContext.User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new { total_count = 0, changes = Array.Empty<object>() };
            }

            criteria.RequestedByUserId = userId;
        }

        var result = await pendingChangeSearchService.SearchAsync(criteria);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changes = new System.Collections.Generic.List<McpPendingChange>();
        foreach (var change in result.Results)
        {
            if (IsRequester(requestContext.User, change) || await approvalService.CanReviewAsync(requestContext.User, change))
            {
                changes.Add(change);
            }
        }

        return new
        {
            total_count = result.TotalCount,
            changes = changes.Select(x => new
            {
                change_id = x.Id,
                tool_name = x.ToolName,
                status = x.IsExpired(now) ? McpPendingChangeStatus.Expired : x.Status,
                requested_by = x.RequestedByUserName,
                requested_at = x.CreatedDate,
                expires_at = x.ExpirationDate,
                reviewed_by = x.ReviewedByUserName,
            }),
        };
    }

    [McpServerTool(Name = ModuleConstants.ToolNames.GetPendingChange, ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Get one change submitted for approval: the tool, its exact arguments, the dry-run preview, " +
        "and, once reviewed, the reviewer and the execution result. A user who is not an approver gets only their own changes.")]
    public async Task<object> GetPendingChange(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Id of the change.")] string change_id)
    {
        requestContext.SetAuditPendingChangeId(change_id);
        var change = await GetVisibleChangeAsync(requestContext.User, change_id);

        return ToDetails(change);
    }

    [McpServerTool(Name = ModuleConstants.ToolNames.ApprovePendingChange, ReadOnly = false, Destructive = true, Idempotent = false)]
    [McpApproverOnly]
    [Description("Approve a pending change and execute it right away with the stored arguments, as the approving user. " +
        "Only approve after the user has reviewed the change with get_pending_change and explicitly asked to approve it. " +
        "A change cannot be approved by the user who requested it, and the approver must be allowed to run the tool.")]
    public async Task<object> ApprovePendingChange(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Id of the change.")] string change_id,
        [Description("Optional note recorded with the approval.")] string comment = null)
    {
        requestContext.SetAuditPendingChangeId(change_id);
        var change = await GetScopeChangeAsync(change_id);
        var user = requestContext.User;

        if (!await IsApproverAsync(user) || !await approvalService.CanReviewAsync(user, change))
        {
            throw new McpForbiddenException($"You may not review pending change '{change_id}'.");
        }

        if (requestContext.Server.ServerOptions.ToolCollection?.TryGetPrimitive(change.ToolName, out var tool) != true)
        {
            throw new McpException($"Tool '{change.ToolName}' of pending change '{change_id}' is not available now: it was removed or disabled by the administrator.");
        }

        if (!await toolAuthorizationService.IsAuthorizedAsync(user, tool))
        {
            throw new McpException($"You are not allowed to run tool '{change.ToolName}', so you cannot approve its changes.");
        }

        change = await approvalService.ApproveAsync(change_id, user.GetUserId(), user.GetUserName(), comment);

        var result = await pendingChangeExecutor.ExecuteAsync(requestContext, tool, change);
        var succeeded = result.IsError != true;
        change = await approvalService.CompleteAsync(change, JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions), succeeded);

        return ToDetails(change);
    }

    [McpServerTool(Name = ModuleConstants.ToolNames.RejectPendingChange, ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Reject a pending change so that it is never executed. An approver rejects any change; " +
        "any other user can only withdraw a change they requested.")]
    public async Task<object> RejectPendingChange(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Id of the change.")] string change_id,
        [Description("Reason for the rejection, shown to the requester.")] string reason = null)
    {
        requestContext.SetAuditPendingChangeId(change_id);
        var user = requestContext.User;
        await GetVisibleChangeAsync(user, change_id);

        var change = await approvalService.RejectAsync(change_id, user.GetUserId(), user.GetUserName(), reason);

        return ToDetails(change);
    }

    private McpScope Scope => httpContextAccessor.HttpContext?.GetMcpScope() ?? throw new McpException("Pending changes are reviewed in an MCP scope.");

    private async Task<McpPendingChange> GetScopeChangeAsync(string changeId)
    {
        var change = string.IsNullOrEmpty(changeId) ? null : await pendingChangeService.GetByIdAsync(changeId);

        if (change == null || !string.Equals(change.ScopeName, Scope.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Pending change '{changeId}' was not found.");
        }

        return change;
    }

    private async Task<McpPendingChange> GetVisibleChangeAsync(ClaimsPrincipal user, string changeId)
    {
        var change = await GetScopeChangeAsync(changeId);

        if (!IsRequester(user, change) && !(await IsApproverAsync(user) && await approvalService.CanReviewAsync(user, change)))
        {
            throw new McpException($"Pending change '{changeId}' was not found.");
        }

        return change;
    }

    protected virtual Task<bool> IsApproverAsync(ClaimsPrincipal user)
    {
        return toolAuthorizationService.IsApproverAsync(user, Scope);
    }

    private static bool IsRequester(ClaimsPrincipal user, McpPendingChange change)
    {
        var userId = user.GetUserId();

        return !string.IsNullOrEmpty(userId) && string.Equals(change.RequestedByUserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private object ToDetails(McpPendingChange change)
    {
        return new
        {
            change_id = change.Id,
            tool_name = change.ToolName,
            status = change.IsExpired(timeProvider.GetUtcNow().UtcDateTime) ? McpPendingChangeStatus.Expired : change.Status,
            arguments = ParseJson(change.GetMaskedArguments()),
            preview = UnwrapToolResult(change.Preview),
            requested_by = change.RequestedByUserName,
            requested_at = change.CreatedDate,
            expires_at = change.ExpirationDate,
            reviewed_by = change.ReviewedByUserName,
            reviewed_at = change.ReviewedDate,
            review_comment = change.ReviewComment,
            result = UnwrapToolResult(change.Result),
        };
    }

    private static JsonNode ParseJson(string json)
    {
        return string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json);
    }

    // The preview and the result are stored as the tool returned them, a CallToolResult. The approver reads what the tool
    // said: its structured content, or the JSON or the text of its content, with is_error when the tool failed.
    private static JsonNode UnwrapToolResult(string json)
    {
        var node = ParseJson(json);
        if (node is not JsonObject result || !result.ContainsKey("content"))
        {
            return node;
        }

        var value = result["structuredContent"]?.DeepClone() ?? GetContentValue(result["content"] as JsonArray);
        if (result["isError"]?.GetValueKind() != JsonValueKind.True)
        {
            return value;
        }

        return new JsonObject { ["is_error"] = true, ["message"] = value };
    }

    private static JsonNode GetContentValue(JsonArray content)
    {
        var texts = content?
            .OfType<JsonObject>()
            .Where(x => x["type"]?.GetValue<string>() == "text")
            .Select(x => x["text"]?.GetValue<string>())
            .ToList() ?? [];

        if (texts.Count != 1)
        {
            return content?.DeepClone();
        }

        try
        {
            return JsonNode.Parse(texts[0]);
        }
        catch (JsonException)
        {
            return JsonValue.Create(texts[0]);
        }
    }
}
