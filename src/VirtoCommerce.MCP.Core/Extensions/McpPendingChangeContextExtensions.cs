using ModelContextProtocol.Server;

namespace VirtoCommerce.MCP.Core.Extensions;

// While an approved change is executed, the tool runs as the approver. These items tell the tool which change it
// executes and who requested it, for example to let the requester follow a job the change started.
public static class McpPendingChangeContextExtensions
{
    private const string ExecutedChangeIdKey = "VirtoCommerce.MCP.PendingChange.Id";
    private const string RequestedByUserIdKey = "VirtoCommerce.MCP.PendingChange.RequestedByUserId";

    public static void SetExecutedPendingChange(this MessageContext context, string changeId, string requestedByUserId)
    {
        context.Items[ExecutedChangeIdKey] = changeId;
        context.Items[RequestedByUserIdKey] = requestedByUserId;
    }

    // Id of the approved change the tool executes, or null when the tool is called directly.
    public static string GetExecutedPendingChangeId(this MessageContext context)
    {
        return context.Items.TryGetValue(ExecutedChangeIdKey, out var value) ? value as string : null;
    }

    public static string GetPendingChangeRequesterId(this MessageContext context)
    {
        return context.Items.TryGetValue(RequestedByUserIdKey, out var value) ? value as string : null;
    }
}
