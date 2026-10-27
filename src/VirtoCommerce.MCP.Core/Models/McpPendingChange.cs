using System;
using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

public class McpPendingChange : AuditableEntity, ICloneable
{
    public string ScopeName { get; set; }

    public string ToolName { get; set; }

    public string Arguments { get; set; }

    public IList<string> SensitiveArgumentPaths { get; set; } = [];

    public string Preview { get; set; }

    public string Status { get; set; } = McpPendingChangeStatus.Pending;

    public string RequestedByUserId { get; set; }

    public string RequestedByUserName { get; set; }

    public string ReviewedByUserId { get; set; }

    public string ReviewedByUserName { get; set; }

    public DateTime? ReviewedDate { get; set; }

    public string ReviewComment { get; set; }

    public DateTime ExpirationDate { get; set; }

    public string Result { get; set; }

    public bool IsFinal()
    {
        return McpPendingChangeStatus.FinalStatuses.Contains(Status);
    }

    public string GetMaskedArguments()
    {
        return McpSensitiveArguments.Mask(Arguments, SensitiveArgumentPaths);
    }

    public bool IsExpired(DateTime now)
    {
        return Status == McpPendingChangeStatus.Pending && ExpirationDate <= now;
    }

    public virtual object Clone()
    {
        return MemberwiseClone();
    }
}
