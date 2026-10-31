using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using Moq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Security;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpPendingChangeReviewServiceTests : McpTestsBase
{
    private const string ChangeId = "change-1";

    private static readonly McpScope _adminScope = new() { Name = McpScopes.Admin, ApprovalPermission = ModuleConstants.Security.Permissions.Approve };

    private readonly Mock<IMcpPendingChangeService> _pendingChangeService = new();
    private readonly Mock<IMcpApprovalService> _approvalService = new();
    private readonly Mock<IMcpScopeRegistry> _scopeRegistry = new();
    private readonly Mock<McpToolAuthorizationService> _toolAuthorizationService = new(Mock.Of<IAuthorizationPolicyProvider>(), Mock.Of<IAuthorizationService>());
    private readonly Mock<McpToolAvailabilityService> _toolAvailabilityService;
    private readonly Mock<McpAuditWriter> _auditWriter;
    private readonly ConcurrentBag<McpAuditEntry> _auditEntries = [];
    private readonly HttpContextAccessor _httpContextAccessor = new() { HttpContext = new DefaultHttpContext() };
    private (McpPendingChange Change, string Result, bool Succeeded)? _completed;

    public McpPendingChangeReviewServiceTests()
    {
        // GetUserId and GetUserName read the claim types the platform sets at startup
        ClaimsPrincipalExtensions.UserIdClaimTypes = ["sub", ClaimTypes.NameIdentifier];
        ClaimsPrincipalExtensions.UserNameClaimTypes = ["name"];

        _toolAvailabilityService = new Mock<McpToolAvailabilityService>(SettingsManager.Object);
        _auditWriter = new Mock<McpAuditWriter>(Mock.Of<IMcpAuditEntryService>(), SettingsManager.Object, NullLogger<McpAuditWriter>.Instance) { CallBase = true };
        _auditWriter
            .Setup(x => x.WriteAsync(It.IsAny<McpAuditEntry>(), It.IsAny<IDictionary<string, JsonElement>>(), It.IsAny<IEnumerable<string>>()))
            .Callback<McpAuditEntry, IDictionary<string, JsonElement>, IEnumerable<string>>((entry, _, _) => _auditEntries.Add(entry))
            .Returns(Task.CompletedTask);

        _scopeRegistry.Setup(x => x.Scopes).Returns([_adminScope]);
        _scopeRegistry.Setup(x => x.GetScope(McpScopes.Admin)).Returns(_adminScope);
        _toolAuthorizationService.Setup(x => x.IsApproverAsync(It.IsAny<ClaimsPrincipal>(), _adminScope)).ReturnsAsync(true);
        _toolAuthorizationService.Setup(x => x.IsAuthorizedAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<IMcpServerPrimitive>())).ReturnsAsync(true);
        _toolAvailabilityService.Setup(x => x.IsDisabledAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        _approvalService.Setup(x => x.CanReviewAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<McpPendingChange>())).ReturnsAsync(true);
        _approvalService
            .Setup(x => x.ApproveAsync(ChangeId, ApproverId, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string _, string userId, string userName, string comment) =>
            {
                var approved = Change();
                approved.Status = McpPendingChangeStatus.Approved;
                approved.ReviewedByUserId = userId;
                approved.ReviewComment = comment;
                return approved;
            });
        _approvalService
            .Setup(x => x.CompleteAsync(It.IsAny<McpPendingChange>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((McpPendingChange change, string result, bool succeeded) =>
            {
                _completed = (change, result, succeeded);
                var completed = (McpPendingChange)change.Clone();
                completed.Status = succeeded ? McpPendingChangeStatus.Applied : McpPendingChangeStatus.Failed;
                completed.Result = result;
                return completed;
            });
        _approvalService
            .Setup(x => x.RejectAsync(ChangeId, ApproverId, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string _, string userId, string _, string reason) =>
            {
                var rejected = Change();
                rejected.Status = McpPendingChangeStatus.Rejected;
                rejected.ReviewedByUserId = userId;
                rejected.ReviewComment = reason;
                return rejected;
            });

        _approvalService.Setup(x => x.IsSelfApprovalAllowedAsync()).ReturnsAsync(false);
        _pendingChangeService
            .Setup(x => x.GetAsync(It.IsAny<IList<string>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((IList<string> ids, string _, bool _) => ids.Contains(ChangeId) ? [Change()] : []);
    }

    [Fact]
    public async Task ApproveAsync_Should_RunTheToolOutsideAnMcpSession_And_CompleteTheChange()
    {
        // Arrange
        var services = Services(ActivateTool());

        // Act
        var details = await CreateService().ApproveAsync(User(ApproverId), ChangeId, "Looks right", services);

        // Assert
        Assert.NotNull(_completed);
        Assert.True(_completed.Value.Succeeded);
        Assert.Contains("Activated black-friday", _completed.Value.Result);
        Assert.Equal(McpPendingChangeStatus.Applied, details.Status);
        Assert.Contains("Activated black-friday", details.ResultJson);
        Assert.False(details.CanReview);

        var toolAudit = Assert.Single(_auditEntries, x => x.ToolName == ToolName);
        Assert.Equal(McpAuditOutcome.Succeeded, toolAudit.Outcome);
        Assert.Equal(ChangeId, toolAudit.PendingChangeId);
        Assert.Equal(McpPendingChangeReviewService.BackOfficeClientId, toolAudit.ClientId);

        var decisionAudit = Assert.Single(_auditEntries, x => x.ToolName == ModuleConstants.ToolNames.ApprovePendingChange);
        Assert.Equal(McpPendingChangeReviewService.BackOfficeClientId, decisionAudit.ClientId);
        Assert.Equal(ChangeId, decisionAudit.PendingChangeId);
        Assert.Same(_adminScope, _httpContextAccessor.HttpContext.GetMcpScope());
    }

    [Fact]
    public async Task ApproveAsync_Should_RecordAFailedChange_When_TheToolReturnsAnError()
    {
        // Arrange
        var failing = McpServerTool.Create(
            new Func<string, string>(promotion_id => throw new ModelContextProtocol.McpException($"Promotion '{promotion_id}' has ended.")),
            new McpServerToolCreateOptions { Name = ToolName, Metadata = [new McpScopeAttribute(McpScopes.Admin)] });

        // Act
        var details = await CreateService().ApproveAsync(User(ApproverId), ChangeId, null, Services(failing));

        // Assert
        Assert.False(_completed!.Value.Succeeded);
        Assert.Equal(McpPendingChangeStatus.Failed, details.Status);
        Assert.Contains("has ended", details.ResultJson);
    }

    [Fact]
    public async Task ApproveAsync_Should_BeForbidden_When_TheUserIsNotAnApproverOfTheScope()
    {
        // Arrange
        _toolAuthorizationService.Setup(x => x.IsApproverAsync(It.IsAny<ClaimsPrincipal>(), _adminScope)).ReturnsAsync(false);

        // Act
        var action = () => CreateService().ApproveAsync(User(ApproverId), ChangeId, null, Services(ActivateTool()));

        // Assert
        await Assert.ThrowsAsync<McpForbiddenException>(action);
        _approvalService.Verify(x => x.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_Should_BeForbidden_When_TheScopeRulesHideTheChangeFromTheReviewer()
    {
        // Arrange
        _approvalService.Setup(x => x.CanReviewAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<McpPendingChange>())).ReturnsAsync(false);

        // Act
        var action = () => CreateService().ApproveAsync(User(ApproverId), ChangeId, null, Services(ActivateTool()));

        // Assert
        await Assert.ThrowsAsync<McpForbiddenException>(action);
    }

    [Fact]
    public async Task ApproveAsync_Should_NotRunTheChange_When_TheToolIsSwitchedOff()
    {
        // Arrange
        _toolAvailabilityService.Setup(x => x.IsDisabledAsync(McpScopes.Admin, ToolName)).ReturnsAsync(true);

        // Act
        var action = () => CreateService().ApproveAsync(User(ApproverId), ChangeId, null, Services(ActivateTool()));

        // Assert
        var exception = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(action);
        Assert.Contains("not available", exception.Message);
        _approvalService.Verify(x => x.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_Should_BeForbidden_When_TheApproverMayNotRunTheTool()
    {
        // Arrange
        _toolAuthorizationService.Setup(x => x.IsAuthorizedAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<IMcpServerPrimitive>())).ReturnsAsync(false);

        // Act
        var action = () => CreateService().ApproveAsync(User(ApproverId), ChangeId, null, Services(ActivateTool()));

        // Assert
        await Assert.ThrowsAsync<McpForbiddenException>(action);
    }

    [Fact]
    public async Task RejectAsync_Should_RecordTheDecision_WithoutRunningTheTool()
    {
        // Act
        var details = await CreateService().RejectAsync(User(ApproverId), ChangeId, "Budget is spent");

        // Assert
        Assert.Equal(McpPendingChangeStatus.Rejected, details.Status);
        Assert.Equal("Budget is spent", details.Change.ReviewComment);
        Assert.Null(_completed);
        var decisionAudit = Assert.Single(_auditEntries);
        Assert.Equal(ModuleConstants.ToolNames.RejectPendingChange, decisionAudit.ToolName);
    }

    [Fact]
    public async Task GetDetailsAsync_Should_ExplainWhy_When_TheRequesterReviewsTheirOwnChange()
    {
        // Act
        var details = await CreateService().GetDetailsAsync(User(RequesterId), ChangeId);

        // Assert
        Assert.True(details.CanReview);
        Assert.Contains("another user", details.CannotApproveReason);
        Assert.Contains("\"promotion_id\"", details.ArgumentsJson);
        Assert.Contains("\"would_activate\"", details.PreviewJson);
    }

    [Fact]
    public async Task GetDetailsAsync_Should_BeForbidden_When_TheChangeDoesNotExist()
    {
        // Act
        var action = () => CreateService().GetDetailsAsync(User(ApproverId), "unknown");

        // Assert
        await Assert.ThrowsAsync<McpForbiddenException>(action);
    }

    private McpPendingChangeReviewService CreateService()
    {
        var executor = new McpPendingChangeExecutor(_auditWriter.Object, NullLoggerFactory.Instance, NullLogger<McpPendingChangeExecutor>.Instance);

        return new McpPendingChangeReviewService(
            _pendingChangeService.Object,
            _approvalService.Object,
            _scopeRegistry.Object,
            _toolAuthorizationService.Object,
            _toolAvailabilityService.Object,
            executor,
            TimeProvider);
    }

    private McpPendingChange Change()
    {
        return new McpPendingChange
        {
            Id = ChangeId,
            ScopeName = McpScopes.Admin,
            ToolName = ToolName,
            Arguments = """{"promotion_id":"black-friday"}""",
            Preview = """{"content":[{"type":"text","text":"{\"would_activate\":\"black-friday\"}"}]}""",
            Status = McpPendingChangeStatus.Pending,
            RequestedByUserId = RequesterId,
            CreatedDate = TimeProvider.UtcNow,
            ExpirationDate = TimeProvider.UtcNow.AddHours(1),
        };
    }

    private static McpServerTool ActivateTool()
    {
        return McpServerTool.Create(
            (string promotion_id) => $"Activated {promotion_id}",
            new McpServerToolCreateOptions { Name = ToolName, Metadata = [new McpScopeAttribute(McpScopes.Admin)] });
    }

    private ServiceProvider Services(McpServerTool tool)
    {
        return new ServiceCollection()
            .AddSingleton(tool)
            .AddSingleton<IHttpContextAccessor>(_httpContextAccessor)
            .BuildServiceProvider();
    }

    private static ClaimsPrincipal User(string userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim("name", userId),
        ], "test"));
    }
}
