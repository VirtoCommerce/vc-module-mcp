using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VirtoCommerce.MCP.Core.Services;

// A module's own step in the tool call pipeline of one scope, registered with AddMcpCallToolFilter. It runs inside the
// MCP module's pipeline: after the permission checks and the argument validation, within the audit, so the result it
// returns is the one that is audited.
public interface IMcpCallToolFilter
{
    ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken);
}
