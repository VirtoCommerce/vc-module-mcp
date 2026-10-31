using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Events;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.Platform.Core.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Data.GenericCrud;

namespace VirtoCommerce.MCP.Data.Services;

public class McpAuditEntryService(
    Func<IMcpRepository> repositoryFactory,
    IPlatformMemoryCache platformMemoryCache,
    IEventPublisher eventPublisher)
    : CrudService<McpAuditEntry, McpAuditEntryEntity, McpAuditEntryChangingEvent, McpAuditEntryChangedEvent>(repositoryFactory, platformMemoryCache, eventPublisher), IMcpAuditEntryService
{
    protected override Task<IList<McpAuditEntryEntity>> LoadEntities(IRepository repository, IList<string> ids, string responseGroup)
    {
        return ((IMcpRepository)repository).GetAuditEntriesByIdsAsync(ids);
    }
}
