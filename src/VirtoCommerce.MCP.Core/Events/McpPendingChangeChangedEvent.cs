using System.Collections.Generic;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Events;

namespace VirtoCommerce.MCP.Core.Events;

public class McpPendingChangeChangedEvent(
    IEnumerable<GenericChangedEntry<McpPendingChange>> changedEntries)
    : GenericChangedEntryEvent<McpPendingChange>(changedEntries)
{
}
