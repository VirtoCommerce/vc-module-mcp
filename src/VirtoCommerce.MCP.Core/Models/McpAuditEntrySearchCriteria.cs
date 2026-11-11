using System;
using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

public class McpAuditEntrySearchCriteria : SearchCriteriaBase
{
    public string ScopeName { get; set; }

    public string ToolName { get; set; }

    public string UserId { get; set; }

    public IList<string> Outcomes { get; set; }

    public string PendingChangeId { get; set; }

    public string TraceId { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }
}
