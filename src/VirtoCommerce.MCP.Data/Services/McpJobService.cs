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
    private readonly IEventPublisher _eventPublisher = eventPublisher;

    protected override Task<IList<McpJobEntity>> LoadEntities(IRepository repository, IList<string> ids, string responseGroup)
    {
        return ((IMcpRepository)repository).GetJobsByIdsAsync(ids);
    }

    public virtual Task<bool> TryChangeStatusAsync(McpJob job, string expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(job);

        return TryChangeStatusInternalAsync(job, expectedStatus);
    }

    // The update bypasses SaveChangesAsync, so the job's events are published here, as for any other change of a job;
    // McpJobChangedEvent only when the status was still the expected one.
    protected virtual async Task<bool> TryChangeStatusInternalAsync(McpJob job, string expectedStatus)
    {
        var original = (await GetAsync([job.Id])).FirstOrDefault();
        var changedEntries = new[] { new GenericChangedEntry<McpJob>(job, original, EntryState.Modified) };
        await _eventPublisher.Publish(new McpJobChangingEvent(changedEntries));

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

        if (affected == 0)
        {
            return false;
        }

        await _eventPublisher.Publish(new McpJobChangedEvent(changedEntries));

        return true;
    }
}
