using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Events;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.Platform.Caching;
using VirtoCommerce.Platform.Core.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

// A status change bypasses SaveChangesAsync, and still publishes the job's events.
[Trait("Category", "Unit")]
[Collection(PlatformCacheCollection.Name)]
public class McpJobServiceTests : McpTestsBase
{
    private const string JobId = "job-1";

    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly McpJobService _service;

    public McpJobServiceTests()
    {
        var cache = new PlatformMemoryCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CachingOptions { CacheEnabled = true }),
            NullLogger<PlatformMemoryCache>.Instance);

        _service = new McpJobService(CreateRepository, cache, _eventPublisher.Object);
    }

    [Fact]
    public async Task TryChangeStatusAsync_Should_PublishTheJobEvents_When_TheStatusChanges()
    {
        // Arrange
        await SeedAsync(Job(McpJobStatus.Queued));
        var job = new McpJob { Id = JobId, ScopeName = McpScopes.Admin, ToolName = ToolName, Status = McpJobStatus.Running };

        // Act
        var changed = await _service.TryChangeStatusAsync(job, McpJobStatus.Queued);

        // Assert
        Assert.True(changed);
        _eventPublisher.Verify(x => x.Publish(It.Is<McpJobChangingEvent>(e => IsStatusChange(e, McpJobStatus.Queued, McpJobStatus.Running)), default), Times.Once);
        _eventPublisher.Verify(x => x.Publish(It.Is<McpJobChangedEvent>(e => IsStatusChange(e, McpJobStatus.Queued, McpJobStatus.Running)), default), Times.Once);
    }

    [Fact]
    public async Task TryChangeStatusAsync_Should_NotPublishChanged_When_TheStatusWasChangedInTheMeantime()
    {
        // Arrange: the job was cancelled before it started
        await SeedAsync(Job(McpJobStatus.Canceled));
        var job = new McpJob { Id = JobId, ScopeName = McpScopes.Admin, ToolName = ToolName, Status = McpJobStatus.Running };

        // Act
        var changed = await _service.TryChangeStatusAsync(job, McpJobStatus.Queued);

        // Assert
        Assert.False(changed);
        _eventPublisher.Verify(x => x.Publish(It.IsAny<McpJobChangedEvent>(), default), Times.Never);
    }

    private static bool IsStatusChange(GenericChangedEntryEvent<McpJob> e, string oldStatus, string newStatus)
    {
        var entry = e.ChangedEntries.Single();

        return entry.EntryState == EntryState.Modified && entry.OldEntry?.Status == oldStatus && entry.NewEntry.Status == newStatus;
    }

    private McpJobEntity Job(string status)
    {
        return new McpJobEntity
        {
            Id = JobId,
            ScopeName = McpScopes.Admin,
            ToolName = ToolName,
            Status = status,
            UserId = RequesterId,
            CreatedDate = TimeProvider.UtcNow,
        };
    }
}
