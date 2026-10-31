using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Jobs;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Jobs;

public class McpJobDispatcher(
    IServiceProvider serviceProvider,
    IMcpJobService jobService,
    IHttpContextAccessor httpContextAccessor)
    : IMcpJobDispatcher
{
    private const int MaxTitleLength = 512;
    private const int MaxMessageLength = 2048;

    public virtual async Task<McpJobHandle> EnqueueAsync<THandler>(RequestContext<CallToolRequestParams> context, object payload, McpJobRequest request)
        where THandler : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(payload);
        request ??= new McpJobRequest();

        var backgroundJob = serviceProvider.GetService<IBackgroundJob>()
            ?? throw new McpException("Background jobs are not available on this platform, so the bulk operation cannot start. Tell the user to contact the administrator.");

        var handlerPayloadType = GetHandlerPayloadType(typeof(THandler), payload.GetType());

        var job = AbstractTypeFactory<McpJob>.TryCreateInstance();
        job.ScopeName = httpContextAccessor.HttpContext?.GetMcpScope()?.Name ?? McpScopes.Admin;
        job.ToolName = context.Params?.Name ?? typeof(THandler).Name;
        job.Title = Truncate(request.Title ?? job.ToolName, MaxTitleLength);
        job.TotalCount = request.TotalCount;
        job.Preview = request.Preview != null ? System.Text.Json.JsonSerializer.Serialize(request.Preview, McpJsonUtilities.DefaultOptions) : null;
        job.UserId = context.User?.GetUserId();
        job.UserName = context.User?.GetUserName();
        job.PendingChangeId = context.GetExecutedPendingChangeId();
        job.RequestedByUserId = context.GetPendingChangeRequesterId();
        await jobService.SaveChangesAsync([job]);

        var trackedPayload = new McpTrackedJobPayload
        {
            McpJobId = job.Id,
            HandlerType = typeof(THandler).AssemblyQualifiedName,
            HandlerPayloadType = handlerPayloadType.AssemblyQualifiedName,
            PayloadType = payload.GetType().AssemblyQualifiedName,
            Payload = JsonConvert.SerializeObject(payload),
        };

        try
        {
            // The MCP job is updated by the tracking handler only, so it is not saved again here: the job may already be running.
            await backgroundJob.Enqueue<McpTrackedJobHandler>(trackedPayload, new EnqueueOptions
            {
                Title = job.Title,
                Queue = request.Queue,
                MaxRetryAttempts = 0,
                ReportProgress = true,
            });
        }
        catch (Exception ex)
        {
            job.Status = McpJobStatus.Failed;
            job.Message = Truncate(ex.Message, MaxMessageLength);
            await jobService.SaveChangesAsync([job]);
            throw;
        }

        return new McpJobHandle
        {
            JobId = job.Id,
            Status = job.Status,
            Title = job.Title,
            TotalCount = job.TotalCount,
            Preview = request.Preview,
            Message = $"The job runs in the background. Check its progress with {ModuleConstants.ToolNames.GetJobStatus}.",
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        return value?.Length > maxLength ? value[..maxLength] : value;
    }

    private static Type GetHandlerPayloadType(Type handlerType, Type payloadType)
    {
        return handlerType.GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IBackgroundJobHandler<>))
            .Select(x => x.GetGenericArguments()[0])
            .FirstOrDefault(x => x.IsAssignableFrom(payloadType))
            ?? throw new ArgumentException($"{handlerType.Name} does not implement IBackgroundJobHandler<> for payload {payloadType.Name}.", nameof(handlerType));
    }
}
