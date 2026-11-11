using System.Collections.Generic;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Events;

namespace VirtoCommerce.MCP.Core.Events;

public class McpJobChangedEvent(
    IEnumerable<GenericChangedEntry<McpJob>> changedEntries)
    : GenericChangedEntryEvent<McpJob>(changedEntries)
{
}
