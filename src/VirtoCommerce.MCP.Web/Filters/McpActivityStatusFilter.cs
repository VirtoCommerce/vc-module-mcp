using System.Diagnostics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Diagnostics;

namespace VirtoCommerce.MCP.Web.Filters;

// The SDK marks the span of a tool call that returned an error with the error's text, which can carry the caller's
// data past the platform's log levels into traces. The span keeps its Error status with a fixed description; the
// error itself is in the audit log, behind its permission.
public static class McpActivityStatusFilter
{
    public const string ErrorStatusDescription = "The tool returned an error.";

    public static McpMessageFilter Create()
    {
        return next => async (context, cancellationToken) =>
        {
            var isToolCall = context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.ToolsCall };

            await next(context, cancellationToken);

            if (isToolCall &&
                Activity.Current is { Source.Name: McpDiagnostics.SdkActivitySourceName, Status: ActivityStatusCode.Error } activity)
            {
                activity.SetStatus(ActivityStatusCode.Error, ErrorStatusDescription);
            }
        };
    }
}
