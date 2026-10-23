using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.Platform.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Data.Services;

public class McpCleanupService(
    Func<IMcpRepository> repositoryFactory,
    ISettingsManager settingsManager,
    ILogger<McpCleanupService> logger,
    TimeProvider timeProvider)
    : IMcpCleanupService
{
    private const int BatchSize = 1000;

    private static readonly string[] _finalStatuses =
    [
        McpPendingChangeStatus.Applied,
        McpPendingChangeStatus.Failed,
        McpPendingChangeStatus.Rejected,
        McpPendingChangeStatus.Expired,
    ];

    public virtual async Task<McpCleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var auditThreshold = now.AddDays(-await settingsManager.GetValueAsync<int>(ModuleConstants.Settings.Cleanup.AuditRetentionDays));
        var changeThreshold = now.AddDays(-await settingsManager.GetValueAsync<int>(ModuleConstants.Settings.Cleanup.PendingChangeRetentionDays));

        var result = new McpCleanupResult
        {
            DeletedAuditEntries = await DeleteInBatchesAsync(repository => GetObsoleteAuditEntries(repository, auditThreshold), cancellationToken),
            DeletedPendingChanges = await DeleteInBatchesAsync(repository => GetObsoletePendingChanges(repository, changeThreshold), cancellationToken),
        };

        if (result.DeletedAuditEntries > 0)
        {
            GenericCachingRegion<McpAuditEntry>.ExpireRegion();
            GenericSearchCachingRegion<McpAuditEntry>.ExpireRegion();
        }

        if (result.DeletedPendingChanges > 0)
        {
            GenericCachingRegion<McpPendingChange>.ExpireRegion();
            GenericSearchCachingRegion<McpPendingChange>.ExpireRegion();
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("MCP cleanup deleted {AuditEntries} audit entries older than {AuditThreshold:u} and {PendingChanges} pending changes older than {ChangeThreshold:u}",
                result.DeletedAuditEntries, auditThreshold, result.DeletedPendingChanges, changeThreshold);
        }

        return result;
    }

    protected virtual IQueryable<McpAuditEntryEntity> GetObsoleteAuditEntries(IMcpRepository repository, DateTime threshold)
    {
        return repository.AuditEntries.Where(x => x.CreatedDate < threshold);
    }

    protected virtual IQueryable<McpPendingChangeEntity> GetObsoletePendingChanges(IMcpRepository repository, DateTime threshold)
    {
        return repository.PendingChanges.Where(x =>
            (_finalStatuses.Contains(x.Status) && (x.ModifiedDate ?? x.CreatedDate) < threshold) ||
            (x.Status == McpPendingChangeStatus.Pending && x.ExpirationDate < threshold));
    }

    protected virtual async Task<int> DeleteInBatchesAsync<TEntity>(Func<IMcpRepository, IQueryable<TEntity>> query, CancellationToken cancellationToken)
        where TEntity : class, IEntity
    {
        var deleted = 0;

        while (true)
        {
            using var repository = repositoryFactory();

            var ids = await query(repository).OrderBy(x => x.Id).Select(x => x.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return deleted;
            }

            deleted += await query(repository).Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);

            if (ids.Count < BatchSize)
            {
                return deleted;
            }
        }
    }
}
