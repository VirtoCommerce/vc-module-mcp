using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.Platform.Caching;
using VirtoCommerce.Platform.Core.Caching;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Core.GenericCrud;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
[Collection(PlatformCacheCollection.Name)]
public class McpPendingChangeSearchServiceTests : McpTestsBase
{
    private readonly TestSearchService _service;

    public McpPendingChangeSearchServiceTests()
    {
        var cache = new PlatformMemoryCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CachingOptions { CacheEnabled = true }),
            NullLogger<PlatformMemoryCache>.Instance);
        var crudService = new McpPendingChangeService(CreateRepository, cache, Mock.Of<IEventPublisher>());

        _service = new TestSearchService(this, cache, crudService);
    }

    [Theory]
    [InlineData(new[] { McpPendingChangeStatus.Pending }, new[] { "pending" })]
    [InlineData(new[] { McpPendingChangeStatus.Expired }, new[] { "expired", "pending-past-expiration" })]
    [InlineData(new[] { McpPendingChangeStatus.Pending, McpPendingChangeStatus.Expired }, new[] { "expired", "pending", "pending-past-expiration" })]
    [InlineData(new[] { McpPendingChangeStatus.Applied }, new[] { "applied" })]
    [InlineData(new[] { McpPendingChangeStatus.Applied, McpPendingChangeStatus.Pending }, new[] { "applied", "pending" })]
    public async Task FilterByStatus_Should_TreatPendingChangesPastTheirExpirationAsExpired(string[] statuses, string[] expectedIds)
    {
        // Arrange
        var now = TimeProvider.UtcNow;
        await SeedAsync(
            PendingChange("pending", expiration: now.AddHours(1)),
            PendingChange("pending-past-expiration", expiration: now.AddMinutes(-1)),
            PendingChange("expired", McpPendingChangeStatus.Expired, expiration: now.AddHours(-1)),
            PendingChange("applied", McpPendingChangeStatus.Applied, expiration: now.AddMinutes(-1)));
        await using var dbContext = CreateDbContext();

        // Act
        var ids = await _service.Filter(dbContext.Set<McpPendingChangeEntity>(), statuses).Select(x => x.Id).ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedIds, ids.Order().ToArray());
    }

    [Fact]
    public async Task SearchAsync_Should_NotCacheTheResult_When_FilteringByThePendingStatus()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1", expiration: TimeProvider.UtcNow.AddHours(1)));
        var criteria = new McpPendingChangeSearchCriteria { Statuses = [McpPendingChangeStatus.Pending] };
        await _service.SearchAsync(criteria);
        TimeProvider.Advance(TimeSpan.FromHours(2));

        // Act
        var result = await _service.SearchAsync(criteria);

        // Assert
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_Should_CacheTheResult_When_TheFilterDoesNotDependOnTime()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1", McpPendingChangeStatus.Applied));
        var criteria = new McpPendingChangeSearchCriteria { Statuses = [McpPendingChangeStatus.Applied] };
        await _service.SearchAsync(criteria);
        await SeedAsync(PendingChange("change-2", McpPendingChangeStatus.Applied));

        // Act
        var result = await _service.SearchAsync(criteria);

        // Assert
        Assert.Equal(1, result.TotalCount);
    }

    private sealed class TestSearchService(McpTestsBase tests, IPlatformMemoryCache cache, McpPendingChangeService crudService)
        : McpPendingChangeSearchService(tests.CreateRepository, cache, crudService, Options.Create(new CrudOptions()), tests.TimeProvider)
    {
        public IQueryable<McpPendingChangeEntity> Filter(IQueryable<McpPendingChangeEntity> query, IList<string> statuses)
        {
            return FilterByStatus(query, statuses);
        }
    }
}
