using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Jobs;

namespace VirtoCommerce.MCP.Data.BackgroundJobs;

public class McpCleanupJob(
    IMcpCleanupService cleanupService)
    : IBackgroundJobHandler<McpCleanupJobPayload>
{
    public Task Execute(McpCleanupJobPayload payload, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return cleanupService.CleanupAsync(cancellationToken);
    }
}
