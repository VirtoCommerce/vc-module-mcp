using System.Collections.Generic;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Events;

namespace VirtoCommerce.MCP.Core.Events;

public class McpPendingChangeChangingEvent(
    IEnumerable<GenericChangedEntry<McpPendingChange>> changedEntries)
    : GenericChangedEntryEvent<McpPendingChange>(changedEntries)
{
}
