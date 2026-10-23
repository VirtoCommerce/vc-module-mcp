using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
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

[McpServerToolType]
[McpScope(McpScopes.Admin)]
public class McpApprovalTools(
    IMcpPendingChangeService pendingChangeService,
    IMcpPendingChangeSearchService pendingChangeSearchService,
    IMcpApprovalService approvalService,
    IAuthorizationService authorizationService,
    McpToolAuthorizationService toolAuthorizationService,
    McpAuditWriter auditWriter,
    ILogger<McpApprovalTools> logger,
    TimeProvider timeProvider)
{
    private const int MaxTake = 50;

    [McpServerTool(Name = ModuleConstants.Tools.ListPendingChanges, ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("List changes that agents submitted for approval, newest first. An approver sees every change, " +
        "any other user only the changes they requested. Returns compact rows; " +
        "call get_pending_change for the arguments and the dry-run preview of one change.")]
    public async Task<object> ListPendingChanges(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Status filter: Pending (default), Approved, Applied, Failed, Rejected or Expired.")] string status = McpPendingChangeStatus.Pending,
        [Description("Only changes submitted by this tool.")] string tool_name = null,
        [Description("Number of rows to skip.")] int skip = 0,
        [Description("Number of rows to return, at most 50.")] int take = 20)
    {
        var criteria = new McpPendingChangeSearchCriteria
        {
            ScopeName = McpScopes.Admin,
            ToolName = tool_name,
            Statuses = string.IsNullOrEmpty(status) ? null : [status],
            Skip = Math.Max(skip, 0),
            Take = Math.Clamp(take, 1, MaxTake),
        };

        if (!await IsApproverAsync(requestContext.User))
        {
            criteria.RequestedByUserId = requestContext.User.GetUserId();
        }

        var result = await pendingChangeSearchService.SearchAsync(criteria);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return new
        {
            total_count = result.TotalCount,
            changes = result.Results.Select(x => new
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

    [McpServerTool(Name = ModuleConstants.Tools.GetPendingChange, ReadOnly = true, Destructive = false, Idempotent = true)]
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

    [McpServerTool(Name = ModuleConstants.Tools.ApprovePendingChange, ReadOnly = false, Destructive = true, Idempotent = false)]
    [Authorize(ModuleConstants.Security.Permissions.Approve)]
    [Description("Approve a pending change and execute it right away with the stored arguments, as the approving user. " +
        "Only approve after the user has reviewed the change with get_pending_change and explicitly asked to approve it. " +
        "A change cannot be approved by the user who requested it, and the approver must be allowed to run the tool.")]
    public async Task<object> ApprovePendingChange(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Id of the change.")] string change_id,
        [Description("Optional note recorded with the approval.")] string comment = null)
    {
        requestContext.SetAuditPendingChangeId(change_id);
        var change = await GetAdminChangeAsync(change_id);
        var user = requestContext.User;

        if (requestContext.Server.ServerOptions.ToolCollection?.TryGetPrimitive(change.ToolName, out var tool) != true)
        {
            throw new McpException($"Tool '{change.ToolName}' of pending change '{change_id}' is not available any more.");
        }

        if (!await toolAuthorizationService.IsAuthorizedAsync(user, tool))
        {
            throw new McpException($"You are not allowed to run tool '{change.ToolName}', so you cannot approve its changes.");
        }

        change = await approvalService.ApproveAsync(change_id, user.GetUserId(), user.GetUserName(), comment);

        var result = await ExecuteAsync(requestContext, tool, change);
        var succeeded = result.IsError != true;
        change = await approvalService.CompleteAsync(change, JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions), succeeded);

        return ToDetails(change);
    }

    [McpServerTool(Name = ModuleConstants.Tools.RejectPendingChange, ReadOnly = false, Destructive = false, Idempotent = false)]
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

    protected virtual async Task<CallToolResult> ExecuteAsync(
        RequestContext<CallToolRequestParams> requestContext,
        McpServerTool tool,
        McpPendingChange change)
    {
        var audit = auditWriter.Create(change.ScopeName, change.ToolName, requestContext.User);
        audit.PendingChangeId = change.Id;
        var stopwatch = Stopwatch.StartNew();

        Dictionary<string, JsonElement> arguments = null;
        CallToolResult result;
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.Arguments ?? "{}", McpJsonUtilities.DefaultOptions);
            var parameters = new CallToolRequestParams { Name = change.ToolName, Arguments = arguments };
            var jsonRpcRequest = new JsonRpcRequest
            {
                Id = requestContext.JsonRpcRequest.Id,
                Method = RequestMethods.ToolsCall,
                Params = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions),
            };

            var toolContext = new RequestContext<CallToolRequestParams>(requestContext.Server, jsonRpcRequest, parameters)
            {
                Services = requestContext.Services,
                User = requestContext.User,
                MatchedPrimitive = tool,
            };

            result = await tool.InvokeAsync(toolContext, CancellationToken.None);
            audit.Outcome = result.IsError == true ? McpAuditOutcome.Failed : McpAuditOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Approved MCP change {ChangeId} failed in tool {ToolName}", change.Id, change.ToolName);
            audit.Outcome = IsRefusedByTool(ex) ? McpAuditOutcome.Failed : McpAuditOutcome.Error;
            audit.ErrorMessage = ex.Message;
            result = new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = ex is McpException ? ex.Message : $"An unexpected error occurred while executing tool '{change.ToolName}'." }],
            };
        }

        stopwatch.Stop();
        audit.DurationMs = stopwatch.ElapsedMilliseconds;
        await auditWriter.WriteAsync(audit, arguments);

        return result;
    }

    private static bool IsRefusedByTool(Exception exception)
    {
        return exception is McpException and not McpProtocolException;
    }

    private async Task<McpPendingChange> GetAdminChangeAsync(string changeId)
    {
        var change = string.IsNullOrEmpty(changeId) ? null : await pendingChangeService.GetByIdAsync(changeId);

        if (change == null || !string.Equals(change.ScopeName, McpScopes.Admin, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Pending change '{changeId}' was not found.");
        }

        return change;
    }

    private async Task<McpPendingChange> GetVisibleChangeAsync(ClaimsPrincipal user, string changeId)
    {
        var change = await GetAdminChangeAsync(changeId);

        if (!IsRequester(user, change) && !await IsApproverAsync(user))
        {
            throw new McpException($"Pending change '{changeId}' was not found.");
        }

        return change;
    }

    protected virtual async Task<bool> IsApproverAsync(ClaimsPrincipal user)
    {
        var result = await authorizationService.AuthorizeAsync(user, ModuleConstants.Security.Permissions.Approve);

        return result.Succeeded;
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
            arguments = ParseJson(change.Arguments),
            preview = ParseJson(change.Preview),
            requested_by = change.RequestedByUserName,
            requested_at = change.CreatedDate,
            expires_at = change.ExpirationDate,
            reviewed_by = change.ReviewedByUserName,
            reviewed_at = change.ReviewedDate,
            review_comment = change.ReviewComment,
            result = ParseJson(change.Result),
        };
    }

    private static JsonNode ParseJson(string json)
    {
        return string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json);
    }
}
