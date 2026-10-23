using System;
using System.Collections.Generic;
using System.Linq;
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

public class McpAuditEntrySearchService(
    Func<IMcpRepository> repositoryFactory,
    IPlatformMemoryCache platformMemoryCache,
    IMcpAuditEntryService crudService,
    IOptions<CrudOptions> crudOptions)
    : SearchService<McpAuditEntrySearchCriteria, McpAuditEntrySearchResult, McpAuditEntry, McpAuditEntryEntity>(repositoryFactory, platformMemoryCache, crudService, crudOptions), IMcpAuditEntrySearchService
{
    protected override IQueryable<McpAuditEntryEntity> BuildQuery(IRepository repository, McpAuditEntrySearchCriteria criteria)
    {
        var query = ((IMcpRepository)repository).AuditEntries;

        if (!string.IsNullOrEmpty(criteria.ScopeName))
        {
            query = query.Where(x => x.ScopeName == criteria.ScopeName);
        }

        if (!string.IsNullOrEmpty(criteria.ToolName))
        {
            query = query.Where(x => x.ToolName == criteria.ToolName);
        }

        if (!string.IsNullOrEmpty(criteria.UserId))
        {
            query = query.Where(x => x.UserId == criteria.UserId);
        }

        if (!criteria.Outcomes.IsNullOrEmpty())
        {
            query = query.Where(x => criteria.Outcomes.Contains(x.Outcome));
        }

        if (!string.IsNullOrEmpty(criteria.PendingChangeId))
        {
            query = query.Where(x => x.PendingChangeId == criteria.PendingChangeId);
        }

        if (criteria.StartDate != null)
        {
            query = query.Where(x => x.CreatedDate >= criteria.StartDate);
        }

        if (criteria.EndDate != null)
        {
            query = query.Where(x => x.CreatedDate <= criteria.EndDate);
        }

        return query;
    }

    protected override IList<SortInfo> BuildSortExpression(McpAuditEntrySearchCriteria criteria)
    {
        var sortInfos = criteria.SortInfos;

        if (sortInfos.IsNullOrEmpty())
        {
            sortInfos =
            [
                new SortInfo { SortColumn = nameof(McpAuditEntryEntity.CreatedDate), SortDirection = SortDirection.Descending },
                new SortInfo { SortColumn = nameof(McpAuditEntryEntity.Id) },
            ];
        }

        return sortInfos;
    }
}
