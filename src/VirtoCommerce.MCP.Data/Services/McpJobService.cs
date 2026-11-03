using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VirtoCommerce.MCP.Core.Events;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.Platform.Caching;
using VirtoCommerce.Platform.Core.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Data.GenericCrud;

namespace VirtoCommerce.MCP.Data.Services;

public class McpJobService(
    Func<IMcpRepository> repositoryFactory,
    IPlatformMemoryCache platformMemoryCache,
    IEventPublisher eventPublisher)
    : CrudService<McpJob, McpJobEntity, McpJobChangingEvent, McpJobChangedEvent>(repositoryFactory, platformMemoryCache, eventPublisher), IMcpJobService
{
    protected override Task<IList<McpJobEntity>> LoadEntities(IRepository repository, IList<string> ids, string responseGroup)
    {
        return ((IMcpRepository)repository).GetJobsByIdsAsync(ids);
    }

    public virtual Task<bool> TryChangeStatusAsync(McpJob job, string expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(job);

        return TryChangeStatusInternalAsync(job, expectedStatus);
    }

    protected virtual async Task<bool> TryChangeStatusInternalAsync(McpJob job, string expectedStatus)
    {
        int affected;
        using (var repository = repositoryFactory())
        {
            affected = await repository.Jobs
                .Where(x => x.Id == job.Id && x.Status == expectedStatus)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(e => e.Status, job.Status)
                    .SetProperty(e => e.EngineJobId, job.EngineJobId)
                    .SetProperty(e => e.StartedDate, job.StartedDate)
                    .SetProperty(e => e.FinishedDate, job.FinishedDate)
                    .SetProperty(e => e.Message, job.Message));
        }

        GenericCachingRegion<McpJob>.ExpireTokenForKey(job.Id);
        GenericSearchCachingRegion<McpJob>.ExpireRegion();

        return affected > 0;
    }
}
