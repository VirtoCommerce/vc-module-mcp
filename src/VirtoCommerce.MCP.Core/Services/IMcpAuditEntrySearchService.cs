using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.GenericCrud;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpAuditEntrySearchService : ISearchService<McpAuditEntrySearchCriteria, McpAuditEntrySearchResult, McpAuditEntry>
{
}
