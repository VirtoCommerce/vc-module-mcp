using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpCleanupService
{
    Task<McpCleanupResult> CleanupAsync(CancellationToken cancellationToken = default);
}
