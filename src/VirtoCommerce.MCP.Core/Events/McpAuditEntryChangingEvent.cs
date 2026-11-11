using System.Collections.Generic;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Events;

namespace VirtoCommerce.MCP.Core.Events;

public class McpAuditEntryChangingEvent(
    IEnumerable<GenericChangedEntry<McpAuditEntry>> changedEntries)
    : GenericChangedEntryEvent<McpAuditEntry>(changedEntries)
{
}
