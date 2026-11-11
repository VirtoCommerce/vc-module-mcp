using ModelContextProtocol.Server;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpAuditContextExtensions
{
    private const string PendingChangeIdKey = "VirtoCommerce.MCP.Audit.PendingChangeId";

    public static void SetAuditPendingChangeId(this MessageContext context, string changeId)
    {
        context.Items[PendingChangeIdKey] = changeId;
    }

    public static string GetAuditPendingChangeId(this MessageContext context)
    {
        return context.Items.TryGetValue(PendingChangeIdKey, out var changeId) ? changeId as string : null;
    }
}
