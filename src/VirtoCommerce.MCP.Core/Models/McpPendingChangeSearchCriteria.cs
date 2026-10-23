using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

public class McpPendingChangeSearchCriteria : SearchCriteriaBase
{
    public string ScopeName { get; set; }

    public string ToolName { get; set; }

    public IList<string> Statuses { get; set; }

    public string RequestedByUserId { get; set; }
}
