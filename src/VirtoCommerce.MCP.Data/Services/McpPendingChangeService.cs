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

public class McpPendingChangeService(
    Func<IMcpRepository> repositoryFactory,
    IPlatformMemoryCache platformMemoryCache,
    IEventPublisher eventPublisher)
    : CrudService<McpPendingChange, McpPendingChangeEntity, McpPendingChangeChangingEvent, McpPendingChangeChangedEvent>(repositoryFactory, platformMemoryCache, eventPublisher), IMcpPendingChangeService
{
    protected override Task<IList<McpPendingChangeEntity>> LoadEntities(IRepository repository, IList<string> ids, string responseGroup)
    {
        return ((IMcpRepository)repository).GetPendingChangesByIdsAsync(ids);
    }
}
