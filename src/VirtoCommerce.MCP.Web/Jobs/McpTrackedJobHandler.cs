using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VirtoCommerce.MCP.Core.Diagnostics;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Jobs;

namespace VirtoCommerce.MCP.Web.Jobs;

public class McpTrackedJobHandler(
    IServiceProvider serviceProvider,
    IMcpJobService jobService,
    TimeProvider timeProvider,
    ILogger<McpTrackedJobHandler> logger,
    IMcpToolAvailabilityService toolAvailabilityService = null)
    : IBackgroundJobHandler<McpTrackedJobPayload>
{
    private const int MaxMessageLength = 2048;

    public virtual async Task Execute(McpTrackedJobPayload payload, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var job = await jobService.GetByIdAsync(payload.McpJobId);
        if (job == null)
        {
            logger.LogWarning("MCP job {McpJobId} was not found; engine job {JobId} is skipped", payload.McpJobId, context.JobId);
            return;
        }

        // A job canceled while it waited in the queue does not start.
        if (job.Status != McpJobStatus.Queued)
        {
            return;
        }

        // A job whose user, or the requester of its approved change, no longer exists or is locked does not start, as
        // the approval of a change does not.
        var users = await GetUsersAsync(job);
        if (users == null)
        {
            await CancelQueuedJobAsync(job, "Not started: the account it runs for no longer exists or is locked.");
            return;
        }

        // Switching the tool off stops its queued jobs too, not only its new calls.
        if (toolAvailabilityService != null && await IsToolDisabledAsync(job, users))
        {
            await CancelQueuedJobAsync(job, $"Not started: tool '{job.ToolName}' was switched off by the administrator.");
            return;
        }

        job.Status = McpJobStatus.Running;
        job.EngineJobId = context.JobId;
        job.StartedDate = timeProvider.GetUtcNow().UtcDateTime;

        // A cancel that saved Canceled after the job was read wins: the job does not start.
        if (!await jobService.TryChangeStatusAsync(job, McpJobStatus.Queued))
        {
            return;
        }

        try
        {
            var handlerPayloadType = Type.GetType(payload.HandlerPayloadType, throwOnError: true);
            var handlerType = Type.GetType(payload.HandlerType, throwOnError: true);
            var innerPayload = JsonConvert.DeserializeObject(payload.Payload ?? "{}", Type.GetType(payload.PayloadType, throwOnError: true));
            var handler = serviceProvider.GetRequiredService(handlerType);
            var execute = typeof(IBackgroundJobHandler<>).MakeGenericType(handlerPayloadType).GetMethod(nameof(IBackgroundJobHandler<object>.Execute));
            var trackingContext = new TrackingContext(context, job, jobService, timeProvider);

            try
            {
                await (Task)execute!.Invoke(handler, [innerPayload, trackingContext, cancellationToken])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Throw(ex.InnerException);
            }

            await trackingContext.FlushAsync();
            job.Status = McpJobStatus.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = McpJobStatus.Canceled;
            job.Message = Truncate($"Canceled after {job.ProcessedCount ?? 0} of {job.TotalCount?.ToString() ?? "?"} items. {job.Message}".Trim());
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP job {McpJobId} of tool {ToolName} failed", job.Id, job.ToolName);
            job.Status = McpJobStatus.Failed;
            job.Message = Truncate(ex.Message);
            throw;
        }
        finally
        {
            job.FinishedDate = timeProvider.GetUtcNow().UtcDateTime;
            await jobService.SaveChangesAsync([job]);
            McpDiagnostics.RecordJob(job.Status);
        }
    }

    // The users of the job: the user it runs as and, for an approved change, its requester. Null when one of them no
    // longer exists or is locked; empty for a job of no user.
    protected virtual async Task<IList<ClaimsPrincipal>> GetUsersAsync(McpJob job)
    {
        var userIds = new[] { job.UserId, job.RequestedByUserId }.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var principalFactory = serviceProvider.GetService<McpUserPrincipalFactory>();
        if (userIds.Count == 0 || principalFactory == null)
        {
            return [];
        }

        var users = new List<ClaimsPrincipal>();
        foreach (var userId in userIds)
        {
            var user = await principalFactory.CreateAsync(userId);
            if (user == null)
            {
                return null;
            }

            users.Add(user);
        }

        return users;
    }

    // The tool switch for each user of the job; for a job of no user, the switch without one.
    protected virtual async Task<bool> IsToolDisabledAsync(McpJob job, IList<ClaimsPrincipal> users)
    {
        if (users.Count == 0)
        {
            return await toolAvailabilityService.IsDisabledAsync(new McpToolAvailabilityContext(job.ScopeName, job.ToolName));
        }

        foreach (var user in users)
        {
            if (await toolAvailabilityService.IsDisabledAsync(new McpToolAvailabilityContext(job.ScopeName, job.ToolName, user)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task CancelQueuedJobAsync(McpJob job, string message)
    {
        job.Status = McpJobStatus.Canceled;
        job.Message = message;
        job.FinishedDate = timeProvider.GetUtcNow().UtcDateTime;
        await jobService.TryChangeStatusAsync(job, McpJobStatus.Queued);
    }

    private static string Truncate(string value)
    {
        return value?.Length > MaxMessageLength ? value[..MaxMessageLength] : value;
    }

    // Passes the progress of the module's handler to the admin UI and records it on the MCP job, at most every few seconds.
    private sealed class TrackingContext(
        IJobExecutionContext inner,
        McpJob job,
        IMcpJobService jobService,
        TimeProvider timeProvider)
        : IJobExecutionContext, IJobProgress
    {
        private static readonly TimeSpan _saveInterval = TimeSpan.FromSeconds(2);

        private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
        private bool _dirty;

        public string JobId => inner.JobId;

        public IJobProgress Progress => this;

        public IReadOnlyDictionary<string, string> Headers => inner.Headers;

        public async Task Report(JobProgressInfo progress, CancellationToken cancellationToken = default)
        {
            await inner.Progress.Report(progress, cancellationToken);

            job.ProcessedCount = progress.ProcessedCount ?? job.ProcessedCount;
            job.TotalCount = progress.TotalCount ?? job.TotalCount;
            job.Message = Truncate(progress.Message) ?? job.Message;
            _dirty = true;

            var now = timeProvider.GetUtcNow();
            if (now - _lastSave >= _saveInterval)
            {
                _lastSave = now;
                await FlushAsync();
            }
        }

        public async Task FlushAsync()
        {
            if (_dirty)
            {
                _dirty = false;
                await jobService.SaveChangesAsync([job]);
            }
        }
    }
}
