using System;
using System.Linq;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

// A background job started by an MCP tool. The agent follows it with get_job_status.
public class McpJob : AuditableEntity, ICloneable
{
    public string ScopeName { get; set; }

    public string ToolName { get; set; }

    public string Title { get; set; }

    public string Status { get; set; } = McpJobStatus.Queued;

    // Id of the job in the platform background job engine.
    public string EngineJobId { get; set; }

    // The user who ran the tool: the approver, for a change applied after approval.
    public string UserId { get; set; }

    public string UserName { get; set; }

    // The user who submitted the change, when an approved change started the job.
    public string RequestedByUserId { get; set; }

    public string PendingChangeId { get; set; }

    public long? ProcessedCount { get; set; }

    public long? TotalCount { get; set; }

    // Last progress message, or the error of a failed job.
    public string Message { get; set; }

    // JSON preview of the first affected items, as the tool returned it when it started the job.
    public string Preview { get; set; }

    public DateTime? StartedDate { get; set; }

    public DateTime? FinishedDate { get; set; }

    public bool IsFinal()
    {
        return McpJobStatus.FinalStatuses.Contains(Status);
    }

    public virtual object Clone()
    {
        return MemberwiseClone();
    }
}
