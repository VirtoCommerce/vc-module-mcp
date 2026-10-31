using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModelContextProtocol;
using Moq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Events;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.Platform.Core.Events;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
[Collection(PlatformCacheCollection.Name)]
public class McpApprovalServiceTests : McpTestsBase
{
    private readonly Mock<IMcpPendingChangeService> _pendingChangeService = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private const string SensitiveArguments = "{\"contactId\":\"c1\",\"email\":\"a@b.c\"}";
    private const string MaskedArguments = "{\"contactId\":\"c1\",\"email\":\"***\"}";

    private readonly McpApprovalService _service;

    public McpApprovalServiceTests()
    {
        _service = new McpApprovalService(CreateRepository, _pendingChangeService.Object, SettingsManager.Object, _eventPublisher.Object, TimeProvider);
    }

    private McpPendingChangeEntity SensitiveChange(string id)
    {
        var change = PendingChange(id);
        change.Arguments = SensitiveArguments;
        change.SensitiveArgumentPaths = "email";

        return change;
    }

    [Fact]
    public async Task SubmitAsync_Should_StoreThePendingChange_With_TheConfiguredLifetime()
    {
        // Arrange
        SetSetting(ModuleConstants.Settings.Approval.PendingChangeLifetimeHours, 24);
        var change = new McpPendingChange { Id = "client-id", Status = McpPendingChangeStatus.Applied, ToolName = ToolName };

        // Act
        var result = await _service.SubmitAsync(change);

        // Assert
        Assert.Null(result.Id);
        Assert.Equal(McpPendingChangeStatus.Pending, result.Status);
        Assert.Equal(TimeProvider.UtcNow.AddHours(24), result.ExpirationDate);
        _pendingChangeService.Verify(x => x.SaveChangesAsync(It.Is<IList<McpPendingChange>>(changes => changes.Contains(change))), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_Should_MoveTheChangeToApproved_When_AnotherUserApproves()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));

        // Act
        var change = await _service.ApproveAsync("change-1", ApproverId, "approver", "Looks good");

        // Assert
        Assert.Equal(McpPendingChangeStatus.Approved, change.Status);
        Assert.Equal(ApproverId, change.ReviewedByUserId);
        Assert.Equal("Looks good", change.ReviewComment);
        Assert.Equal(TimeProvider.UtcNow, change.ReviewedDate);
        Assert.Equal(McpPendingChangeStatus.Approved, (await GetPendingChangeAsync("change-1")).Status);
        _eventPublisher.Verify(x => x.Publish(It.IsAny<McpPendingChangeChangingEvent>(), default), Times.Once);
        _eventPublisher.Verify(x => x.Publish(It.IsAny<McpPendingChangeChangedEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_Should_ScrubTheSensitiveArguments()
    {
        // Arrange
        await SeedAsync(SensitiveChange("change-1"));

        // Act
        await _service.RejectAsync("change-1", ApproverId, "approver", null);

        // Assert
        Assert.Equal(MaskedArguments, (await GetPendingChangeAsync("change-1")).Arguments);
    }

    [Fact]
    public async Task CompleteAsync_Should_ScrubTheSensitiveArguments_That_TheApprovalKept()
    {
        // Arrange
        await SeedAsync(SensitiveChange("change-1"));
        var approved = await _service.ApproveAsync("change-1", ApproverId, "approver", null);
        var argumentsAfterApproval = (await GetPendingChangeAsync("change-1")).Arguments;

        // Act
        await _service.CompleteAsync(approved, "{}", succeeded: true);

        // Assert
        Assert.Equal(SensitiveArguments, argumentsAfterApproval);
        Assert.Equal(MaskedArguments, (await GetPendingChangeAsync("change-1")).Arguments);
    }

    [Fact]
    public async Task ExpireOverdueAsync_Should_ExpireOverdueChanges_And_ScrubTheirSensitiveArguments()
    {
        // Arrange
        var overdue = SensitiveChange("overdue");
        overdue.ExpirationDate = TimeProvider.UtcNow.AddMinutes(-1);
        await SeedAsync(overdue, SensitiveChange("active"));

        // Act
        var expired = await _service.ExpireOverdueAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, expired);
        var stored = await GetPendingChangeAsync("overdue");
        Assert.Equal(McpPendingChangeStatus.Expired, stored.Status);
        Assert.Equal(MaskedArguments, stored.Arguments);
        Assert.Equal(McpPendingChangeStatus.Pending, (await GetPendingChangeAsync("active")).Status);
    }

    [Fact]
    public async Task ApproveAsync_Should_RefuseTheRequester_By_Default()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => _service.ApproveAsync("change-1", RequesterId, "requester", null));

        // Assert
        Assert.Contains("another user", exception.Message);
        Assert.Equal(McpPendingChangeStatus.Pending, (await GetPendingChangeAsync("change-1")).Status);
    }

    [Fact]
    public async Task ApproveAsync_Should_AllowTheRequester_When_SelfApprovalIsEnabled()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));
        SetSetting(ModuleConstants.Settings.Approval.AllowSelfApproval, true);

        // Act
        var change = await _service.ApproveAsync("change-1", RequesterId, "requester", null);

        // Assert
        Assert.Equal(McpPendingChangeStatus.Approved, change.Status);
    }

    [Fact]
    public async Task ApproveAsync_Should_RefuseASecondApproval()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));
        await _service.ApproveAsync("change-1", ApproverId, "approver", null);

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => _service.ApproveAsync("change-1", "another-approver", "another", null));

        // Assert
        Assert.Contains("already approved", exception.Message);
    }

    [Fact]
    public async Task ApproveAsync_Should_MarkTheChangeExpired_And_Refuse_When_ItsLifetimeIsOver()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));
        TimeProvider.Advance(TimeSpan.FromHours(73));

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => _service.ApproveAsync("change-1", ApproverId, "approver", null));

        // Assert
        Assert.Contains("expired", exception.Message);
        Assert.Equal(McpPendingChangeStatus.Expired, (await GetPendingChangeAsync("change-1")).Status);
    }

    [Fact]
    public async Task ApproveAsync_Should_Refuse_When_TheChangeDoesNotExist()
    {
        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => _service.ApproveAsync("missing", ApproverId, "approver", null));

        // Assert
        Assert.Contains("was not found", exception.Message);
    }

    [Fact]
    public async Task RejectAsync_Should_LetTheRequesterWithdrawTheChange_And_PreventItsApproval()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));

        // Act
        var rejected = await _service.RejectAsync("change-1", RequesterId, "requester", "Changed my mind");
        var exception = await Assert.ThrowsAsync<McpException>(() => _service.ApproveAsync("change-1", ApproverId, "approver", null));

        // Assert
        Assert.Equal(McpPendingChangeStatus.Rejected, rejected.Status);
        Assert.Equal("Changed my mind", rejected.ReviewComment);
        Assert.Contains("already rejected", exception.Message);
    }

    [Theory]
    [InlineData(true, McpPendingChangeStatus.Applied)]
    [InlineData(false, McpPendingChangeStatus.Failed)]
    public async Task CompleteAsync_Should_RecordTheResultOfAnApprovedChange(bool succeeded, string expectedStatus)
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));
        var approved = await _service.ApproveAsync("change-1", ApproverId, "approver", null);

        // Act
        var completed = await _service.CompleteAsync(approved, "{\"content\":[]}", succeeded);

        // Assert
        Assert.Equal(expectedStatus, completed.Status);
        Assert.Equal("{\"content\":[]}", completed.Result);
        Assert.Equal(expectedStatus, (await GetPendingChangeAsync("change-1")).Status);
    }

    [Fact]
    public async Task CompleteAsync_Should_RefuseAChangeThatIsAlreadyCompleted()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));
        var approved = await _service.ApproveAsync("change-1", ApproverId, "approver", null);
        await _service.CompleteAsync(approved, "{}", succeeded: true);

        // Act & Assert
        await Assert.ThrowsAsync<McpException>(() => _service.CompleteAsync(approved, "{}", succeeded: true));
    }

    [Fact]
    public async Task CompleteAsync_Should_RefuseAChangeThatWasNotApproved()
    {
        // Arrange
        await SeedAsync(PendingChange("change-1"));
        var change = new McpPendingChange { Id = "change-1", Status = McpPendingChangeStatus.Pending };

        // Act & Assert
        await Assert.ThrowsAsync<McpException>(() => _service.CompleteAsync(change, "{}", succeeded: true));
    }
}
