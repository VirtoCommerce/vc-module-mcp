using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
[Collection(PlatformCacheCollection.Name)]
public class McpCleanupServiceTests : McpTestsBase
{
    private readonly McpCleanupService _service;

    public McpCleanupServiceTests()
    {
        _service = new McpCleanupService(CreateRepository, SettingsManager.Object, NullLogger<McpCleanupService>.Instance, TimeProvider);
    }

    private DateTime OlderThanRetention => TimeProvider.UtcNow.AddDays(-100);

    private DateTime WithinRetention => TimeProvider.UtcNow.AddDays(-10);

    [Fact]
    public async Task CleanupAsync_Should_DeleteOnlyAuditEntriesOlderThanTheRetention()
    {
        // Arrange
        await SeedAsync(AuditEntry("old-1", OlderThanRetention), AuditEntry("old-2", OlderThanRetention), AuditEntry("recent", WithinRetention));

        // Act
        var result = await _service.CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.DeletedAuditEntries);
        Assert.Equal(["recent"], await GetIdsAsync<McpAuditEntryEntity>());
    }

    [Fact]
    public async Task CleanupAsync_Should_DeleteReviewedAndLongExpiredChanges_And_KeepApprovedAndRecentOnes()
    {
        // Arrange
        var now = TimeProvider.UtcNow;
        await SeedAsync(
            PendingChange("applied-old", McpPendingChangeStatus.Applied, created: OlderThanRetention, modified: OlderThanRetention),
            PendingChange("rejected-old-never-modified", McpPendingChangeStatus.Rejected, created: OlderThanRetention),
            PendingChange("applied-recent", McpPendingChangeStatus.Applied, created: OlderThanRetention, modified: WithinRetention),
            PendingChange("pending-expired-long-ago", created: OlderThanRetention, expiration: OlderThanRetention),
            PendingChange("pending-expired-recently", created: WithinRetention, expiration: now.AddDays(-1)),
            PendingChange("pending-active", created: now, expiration: now.AddDays(1)),
            PendingChange("approved-never-completed", McpPendingChangeStatus.Approved, created: OlderThanRetention, modified: OlderThanRetention));

        // Act
        var result = await _service.CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.DeletedPendingChanges);
        Assert.Equal(
            ["applied-recent", "approved-never-completed", "pending-active", "pending-expired-recently"],
            await GetIdsAsync<McpPendingChangeEntity>());
    }

    [Fact]
    public async Task CleanupAsync_Should_DeleteMoreThanOneBatch()
    {
        // Arrange
        await SeedAsync(Enumerable.Range(0, 1500).Select(i => (object)AuditEntry($"old-{i:D4}", OlderThanRetention)).ToArray());

        // Act
        var result = await _service.CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1500, result.DeletedAuditEntries);
        Assert.Empty(await GetIdsAsync<McpAuditEntryEntity>());
    }

    [Fact]
    public async Task CleanupAsync_Should_UseTheConfiguredRetention()
    {
        // Arrange
        await SeedAsync(AuditEntry("ten-days-old", WithinRetention));
        SetSetting(ModuleConstants.Settings.Cleanup.AuditRetentionDays, 7);

        // Act
        var result = await _service.CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.DeletedAuditEntries);
    }
}
