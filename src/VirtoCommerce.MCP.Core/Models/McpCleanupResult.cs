namespace VirtoCommerce.MCP.Core.Models;

public class McpCleanupResult
{
    public int DeletedAuditEntries { get; set; }

    public int DeletedPendingChanges { get; set; }

    public int ExpiredPendingChanges { get; set; }

    public int DeletedJobs { get; set; }
}
