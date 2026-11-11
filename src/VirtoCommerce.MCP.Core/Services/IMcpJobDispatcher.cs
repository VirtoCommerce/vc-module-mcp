using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

// Starts the background work of a bulk tool and tracks it: the agent gets a job handle at once and follows the job
// with get_job_status. The handler is an ordinary platform background job handler, registered with AddBackgroundJob;
// its progress reports become the progress of the MCP job.
public interface IMcpJobDispatcher
{
    Task<McpJobHandle> EnqueueAsync<THandler>(RequestContext<CallToolRequestParams> context, object payload, McpJobRequest request)
        where THandler : class;
}
