using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Data.Repositories;

public interface IMcpRepository : IRepository
{
    IQueryable<McpPendingChangeEntity> PendingChanges { get; }

    IQueryable<McpAuditEntryEntity> AuditEntries { get; }

    IQueryable<McpJobEntity> Jobs { get; }

    Task<IList<McpPendingChangeEntity>> GetPendingChangesByIdsAsync(IList<string> ids);

    Task<IList<McpAuditEntryEntity>> GetAuditEntriesByIdsAsync(IList<string> ids);

    Task<IList<McpJobEntity>> GetJobsByIdsAsync(IList<string> ids);
}
