using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.MCP.Data.Repositories;

public class McpRepository(
    McpDbContext dbContext)
    : DbContextRepositoryBase<McpDbContext>(dbContext), IMcpRepository
{
    public IQueryable<McpPendingChangeEntity> PendingChanges => DbContext.Set<McpPendingChangeEntity>();

    public IQueryable<McpAuditEntryEntity> AuditEntries => DbContext.Set<McpAuditEntryEntity>();

    public IQueryable<McpJobEntity> Jobs => DbContext.Set<McpJobEntity>();

    public async Task<IList<McpPendingChangeEntity>> GetPendingChangesByIdsAsync(IList<string> ids)
    {
        return await PendingChanges.Where(x => ids.Contains(x.Id)).ToListAsync();
    }

    public async Task<IList<McpAuditEntryEntity>> GetAuditEntriesByIdsAsync(IList<string> ids)
    {
        return await AuditEntries.Where(x => ids.Contains(x.Id)).ToListAsync();
    }

    public async Task<IList<McpJobEntity>> GetJobsByIdsAsync(IList<string> ids)
    {
        return await Jobs.Where(x => ids.Contains(x.Id)).ToListAsync();
    }
}
