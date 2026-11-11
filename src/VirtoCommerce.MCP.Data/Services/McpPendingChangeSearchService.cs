using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.Platform.Core.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.GenericCrud;
using VirtoCommerce.Platform.Data.GenericCrud;

namespace VirtoCommerce.MCP.Data.Services;

public class McpPendingChangeSearchService(
    Func<IMcpRepository> repositoryFactory,
    IPlatformMemoryCache platformMemoryCache,
    IMcpPendingChangeService crudService,
    IOptions<CrudOptions> crudOptions,
    TimeProvider timeProvider)
    : SearchService<McpPendingChangeSearchCriteria, McpPendingChangeSearchResult, McpPendingChange, McpPendingChangeEntity>(repositoryFactory, platformMemoryCache, crudService, crudOptions), IMcpPendingChangeSearchService
{
    public override async Task<McpPendingChangeSearchResult> SearchAsync(McpPendingChangeSearchCriteria criteria, bool clone = true)
    {
        if (!IsTimeDependent(criteria))
        {
            return await base.SearchAsync(criteria, clone);
        }

        using (CacheDisabler.DisableCache())
        {
            return await base.SearchAsync(criteria, clone);
        }
    }

    protected override IQueryable<McpPendingChangeEntity> BuildQuery(IRepository repository, McpPendingChangeSearchCriteria criteria)
    {
        var query = ((IMcpRepository)repository).PendingChanges;

        if (!string.IsNullOrEmpty(criteria.ScopeName))
        {
            query = query.Where(x => x.ScopeName == criteria.ScopeName);
        }

        if (!criteria.ScopeNames.IsNullOrEmpty())
        {
            query = query.Where(x => criteria.ScopeNames.Contains(x.ScopeName));
        }

        if (!string.IsNullOrEmpty(criteria.ToolName))
        {
            query = query.Where(x => x.ToolName == criteria.ToolName);
        }

        if (!criteria.Statuses.IsNullOrEmpty())
        {
            query = FilterByStatus(query, criteria.Statuses);
        }

        if (!string.IsNullOrEmpty(criteria.RequestedByUserId))
        {
            query = query.Where(x => x.RequestedByUserId == criteria.RequestedByUserId);
        }

        return query;
    }

    protected virtual IQueryable<McpPendingChangeEntity> FilterByStatus(IQueryable<McpPendingChangeEntity> query, IList<string> statuses)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var storedStatuses = statuses.Except([McpPendingChangeStatus.Pending, McpPendingChangeStatus.Expired]).ToList();
        var conditions = new List<Expression<Func<McpPendingChangeEntity, bool>>>();

        if (storedStatuses.Count > 0)
        {
            conditions.Add(x => storedStatuses.Contains(x.Status));
        }

        if (statuses.Contains(McpPendingChangeStatus.Pending))
        {
            conditions.Add(x => x.Status == McpPendingChangeStatus.Pending && x.ExpirationDate > now);
        }

        if (statuses.Contains(McpPendingChangeStatus.Expired))
        {
            conditions.Add(x => x.Status == McpPendingChangeStatus.Expired || (x.Status == McpPendingChangeStatus.Pending && x.ExpirationDate <= now));
        }

        return query.Where(conditions.Or());
    }

    protected static bool IsTimeDependent(McpPendingChangeSearchCriteria criteria)
    {
        return criteria.Statuses?.Any(x => x is McpPendingChangeStatus.Pending or McpPendingChangeStatus.Expired) == true;
    }

    protected override IList<SortInfo> BuildSortExpression(McpPendingChangeSearchCriteria criteria)
    {
        var sortInfos = criteria.SortInfos;

        if (sortInfos.IsNullOrEmpty())
        {
            sortInfos =
            [
                new SortInfo { SortColumn = nameof(McpPendingChangeEntity.CreatedDate), SortDirection = SortDirection.Descending },
                new SortInfo { SortColumn = nameof(McpPendingChangeEntity.Id) },
            ];
        }

        return sortInfos;
    }
}
