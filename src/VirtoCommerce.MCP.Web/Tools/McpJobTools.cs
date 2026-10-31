using System;
using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
using VirtoCommerce.Platform.Core.Jobs;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Tools;

[McpServerToolType]
[McpScope(McpScopes.Admin, McpScopes.Vendor)]
public class McpJobTools(
    IMcpJobService jobService,
    McpToolAuthorizationService toolAuthorizationService,
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider,
    TimeProvider timeProvider)
{
    [McpServerTool(Name = ModuleConstants.ToolNames.GetJobStatus, ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Get the status and progress of a background job that a bulk tool started: Queued, Running, Succeeded, Failed or Canceled, " +
        "how many items are processed, and the last message. Call it again later while the job is Queued or Running.")]
    public async Task<object> GetJobStatus(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Id of the job, returned by the bulk tool as job_id.")] string job_id)
    {
        var job = await GetVisibleJobAsync(requestContext.User, job_id);

        return new
        {
            job_id = job.Id,
            tool_name = job.ToolName,
            title = job.Title,
            status = job.Status,
            processed_count = job.ProcessedCount,
            total_count = job.TotalCount,
            percent = GetPercent(job),
            message = job.Message,
            started_by = job.UserName,
            created_at = job.CreatedDate,
            started_at = job.StartedDate,
            finished_at = job.FinishedDate,
            pending_change_id = job.PendingChangeId,
            preview = string.IsNullOrEmpty(job.Preview) ? null : JsonNode.Parse(job.Preview),
        };
    }

    [McpServerTool(Name = ModuleConstants.ToolNames.CancelJob, ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Cancel a background job that a bulk tool started. A queued job does not start; a running job stops when the " +
        "job engine supports cancellation, and the items processed so far stay processed. Ask the user before canceling.")]
    public async Task<object> CancelJob(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Id of the job, returned by the bulk tool as job_id.")] string job_id)
    {
        var job = await GetVisibleJobAsync(requestContext.User, job_id);
        if (job.IsFinal())
        {
            throw new McpException($"Job '{job_id}' is already {job.Status}.");
        }

        if (job.Status == McpJobStatus.Queued)
        {
            job.Status = McpJobStatus.Canceled;
            job.Message = "Canceled before it started.";
            job.FinishedDate = timeProvider.GetUtcNow().UtcDateTime;
            await jobService.SaveChangesAsync([job]);

            return new { job_id = job.Id, status = job.Status, message = job.Message };
        }

        var backgroundJob = serviceProvider.GetService<IBackgroundJob>();
        if (backgroundJob?.SupportsCancellation != true || string.IsNullOrEmpty(job.EngineJobId) || !await backgroundJob.Cancel(job.EngineJobId))
        {
            throw new McpException($"Job '{job_id}' is running and the background job engine cannot stop it. It will finish on its own.");
        }

        return new
        {
            job_id = job.Id,
            status = job.Status,
            message = "Cancellation requested. The job stops after the item it is processing; check it with get_job_status.",
        };
    }

    private async Task<McpJob> GetVisibleJobAsync(ClaimsPrincipal user, string jobId)
    {
        var job = string.IsNullOrEmpty(jobId) ? null : await jobService.GetByIdAsync(jobId);
        var scopeName = httpContextAccessor.HttpContext?.GetMcpScope()?.Name;

        if (job == null || !string.Equals(job.ScopeName, scopeName, StringComparison.OrdinalIgnoreCase) || !await CanSeeAsync(user, job))
        {
            throw new McpException($"Job '{jobId}' was not found.");
        }

        return job;
    }

    // The user who ran the tool, the user whose approved change started the job, and the approvers of the scope.
    protected virtual async Task<bool> CanSeeAsync(ClaimsPrincipal user, McpJob job)
    {
        var userId = user.GetUserId();
        if (!string.IsNullOrEmpty(userId) &&
            (string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase) || string.Equals(job.RequestedByUserId, userId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return await toolAuthorizationService.IsApproverAsync(user, httpContextAccessor.HttpContext?.GetMcpScope());
    }

    // Share of the items processed, or null when the job does not know its total
    private static int? GetPercent(McpJob job)
    {
        const int Whole = 100;

        if (job.TotalCount is not > 0 || job.ProcessedCount == null)
        {
            return null;
        }

        return (int)Math.Min(Whole, job.ProcessedCount.Value * Whole / job.TotalCount.Value);
    }
}
