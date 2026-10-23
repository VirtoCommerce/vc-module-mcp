using System;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

public class McpAuditEntry : AuditableEntity, ICloneable
{
    public string ScopeName { get; set; }

    public string ToolName { get; set; }

    public string UserId { get; set; }

    public string UserName { get; set; }

    public string ClientId { get; set; }

    public string Arguments { get; set; }

    public string Outcome { get; set; }

    public string ErrorMessage { get; set; }

    public long DurationMs { get; set; }

    public string PendingChangeId { get; set; }

    public virtual object Clone()
    {
        return MemberwiseClone();
    }
}
