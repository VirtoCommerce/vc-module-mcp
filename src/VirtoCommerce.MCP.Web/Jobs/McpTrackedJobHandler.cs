using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Jobs;

namespace VirtoCommerce.MCP.Web.Jobs;

public class McpTrackedJobHandler(
    IServiceProvider serviceProvider,
    IMcpJobService jobService,
    TimeProvider timeProvider,
    ILogger<McpTrackedJobHandler> logger)
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
        if (job.Status == McpJobStatus.Canceled)
        {
            return;
        }

        job.Status = McpJobStatus.Running;
        job.EngineJobId = context.JobId;
        job.StartedDate = timeProvider.GetUtcNow().UtcDateTime;
        await jobService.SaveChangesAsync([job]);

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
        }
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
