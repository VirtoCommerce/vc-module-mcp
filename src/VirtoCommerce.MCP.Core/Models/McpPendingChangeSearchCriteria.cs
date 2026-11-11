using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

public class McpPendingChangeSearchCriteria : SearchCriteriaBase
{
    public string ScopeName { get; set; }

    // Changes of any of these scopes, for a reviewer who approves in several scopes.
    public IList<string> ScopeNames { get; set; }

    public string ToolName { get; set; }

    public IList<string> Statuses { get; set; }

    public string RequestedByUserId { get; set; }
}
