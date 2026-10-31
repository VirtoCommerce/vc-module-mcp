using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Moq;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Security;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpConnectedAgentServiceTests
{
    private const string McpApp = "app-claude";
    private const string AdminApp = "app-admin-spa";
    private const string Marketer = "user-marketer";
    private const string Approver = "user-approver";

    private static readonly DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOpenIddictApplicationManager> _applications = new();
    private readonly Mock<IOpenIddictAuthorizationManager> _authorizations = new();
    private readonly Mock<IOpenIddictTokenManager> _tokens = new();
    private readonly Mock<IUserSessionsService> _sessions = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager =
        new(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

    public McpConnectedAgentServiceTests()
    {
        // GetUserId reads the claim types the platform sets at startup
        ClaimsPrincipalExtensions.UserIdClaimTypes = ["sub", ClaimTypes.NameIdentifier];

        _applications.Setup(x => x.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(AsAsync<object>(McpApp, AdminApp));

        Application(McpApp, "claude-code", "Claude Code", "ept:authorization", "rsrc:https://admin.example.com/admin/mcp");
        Application(AdminApp, "admin-spa", "Back office", "ept:authorization", "rsrc:https://admin.example.com/api");

        Authorization("auth-marketer", McpApp, Marketer, OpenIddictConstants.Statuses.Valid, _now.AddDays(-3),
            Token("t1", _now.AddDays(-3), OpenIddictConstants.TokenTypeIdentifiers.RefreshToken, OpenIddictConstants.Statuses.Redeemed),
            Token("t2", _now.AddHours(-1), OpenIddictConstants.TokenTypeIdentifiers.RefreshToken, OpenIddictConstants.Statuses.Valid));
        Authorization("auth-approver", McpApp, Approver, OpenIddictConstants.Statuses.Valid, _now.AddDays(-10),
            Token("t3", _now.AddDays(-9), OpenIddictConstants.TokenTypeIdentifiers.RefreshToken, OpenIddictConstants.Statuses.Valid));
        Authorization("auth-revoked", McpApp, Marketer, OpenIddictConstants.Statuses.Revoked, _now.AddDays(-30));
        Authorization("auth-backoffice", AdminApp, Marketer, OpenIddictConstants.Statuses.Valid, _now.AddDays(-1));

        _authorizations.Setup(x => x.FindByApplicationIdAsync(McpApp, It.IsAny<CancellationToken>()))
            .Returns(AsAsync<object>("auth-marketer", "auth-approver", "auth-revoked"));
        _authorizations.Setup(x => x.FindByApplicationIdAsync(AdminApp, It.IsAny<CancellationToken>()))
            .Returns(AsAsync<object>("auth-backoffice"));

        _userManager.Setup(x => x.FindByIdAsync(Marketer)).ReturnsAsync(new ApplicationUser { Id = Marketer, UserName = "mcp-marketer" });
        _userManager.Setup(x => x.FindByIdAsync(Approver)).ReturnsAsync(new ApplicationUser { Id = Approver, UserName = "mcp-approver" });
    }

    [Fact]
    public async Task SearchAsync_Should_ListTheValidAuthorizationsOfMcpClients_Only()
    {
        // Act
        var result = await CreateService().SearchAsync(new McpConnectedAgentSearchCriteria());

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["auth-marketer", "auth-approver"], result.Results.Select(x => x.Id));

        var marketer = result.Results[0];
        Assert.Equal("claude-code", marketer.ClientId);
        Assert.Equal("Claude Code", marketer.ClientName);
        Assert.Equal("mcp-marketer", marketer.UserName);
        Assert.Equal(["https://admin.example.com/admin/mcp"], marketer.Resources);
        Assert.Equal(_now.AddDays(-3).UtcDateTime, marketer.ConnectedDate);
        Assert.Equal(_now.AddHours(-1).UtcDateTime, marketer.LastUsedDate);
        Assert.Equal(1, marketer.ActiveSessionCount);
    }

    [Fact]
    public async Task SearchAsync_Should_FilterByUser()
    {
        // Act
        var result = await CreateService().SearchAsync(new McpConnectedAgentSearchCriteria { UserId = Approver });

        // Assert
        var agent = Assert.Single(result.Results);
        Assert.Equal("auth-approver", agent.Id);
    }

    [Fact]
    public async Task RevokeAsync_Should_SignTheAgentOut_When_TheUserRevokesTheirOwnAgent()
    {
        // Act
        var result = await CreateService().RevokeAsync(User(Marketer), "auth-marketer", canRevokeAny: false);

        // Assert
        Assert.Equal(McpConnectedAgentRevokeResult.Revoked, result);
        _sessions.Verify(x => x.TerminateUserSessionGroup("auth-marketer"), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_Should_BeForbidden_When_TheAgentIsAnotherUsers_And_TheUserMayNotManageAgents()
    {
        // Act
        var result = await CreateService().RevokeAsync(User(Marketer), "auth-approver", canRevokeAny: false);

        // Assert
        Assert.Equal(McpConnectedAgentRevokeResult.Forbidden, result);
        _sessions.Verify(x => x.TerminateUserSessionGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_Should_RevokeAnyUsersAgent_When_TheUserManagesAgents()
    {
        // Act
        var result = await CreateService().RevokeAsync(User(Marketer), "auth-approver", canRevokeAny: true);

        // Assert
        Assert.Equal(McpConnectedAgentRevokeResult.Revoked, result);
        _sessions.Verify(x => x.TerminateUserSessionGroup("auth-approver"), Times.Once);
    }

    [Theory]
    [InlineData("auth-backoffice")]
    [InlineData("unknown")]
    [InlineData(null)]
    public async Task RevokeAsync_Should_NotTouchSessions_That_AreNotOfAnMcpClient(string authorizationId)
    {
        // Act
        var result = await CreateService().RevokeAsync(User(Marketer), authorizationId, canRevokeAny: true);

        // Assert
        Assert.Equal(McpConnectedAgentRevokeResult.NotFound, result);
        _sessions.Verify(x => x.TerminateUserSessionGroup(It.IsAny<string>()), Times.Never);
    }

    private McpConnectedAgentService CreateService()
    {
        return new McpConnectedAgentService(_applications.Object, _authorizations.Object, _tokens.Object, _sessions.Object, () => _userManager.Object);
    }

    private void Application(string id, string clientId, string displayName, params string[] permissions)
    {
        _applications.Setup(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(id);
        _applications.Setup(x => x.GetIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(id);
        _applications.Setup(x => x.GetClientIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(clientId);
        _applications.Setup(x => x.GetDisplayNameAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(displayName);
        _applications.Setup(x => x.GetPermissionsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync([.. permissions]);
    }

    private void Authorization(string id, string applicationId, string subject, string status, DateTimeOffset created, params string[] tokenIds)
    {
        _authorizations.Setup(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(id);
        _authorizations.Setup(x => x.GetIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(id);
        _authorizations.Setup(x => x.GetApplicationIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(applicationId);
        _authorizations.Setup(x => x.GetSubjectAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(subject);
        _authorizations.Setup(x => x.GetCreationDateAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        _authorizations.Setup(x => x.HasStatusAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object _, string expected, CancellationToken _) => expected == status);
        _tokens.Setup(x => x.FindByAuthorizationIdAsync(id, It.IsAny<CancellationToken>())).Returns(AsAsync<object>(tokenIds));
    }

    private string Token(string id, DateTimeOffset created, string type, string status)
    {
        _tokens.Setup(x => x.GetCreationDateAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        _tokens.Setup(x => x.HasTypeAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object _, string expected, CancellationToken _) => expected == type);
        _tokens.Setup(x => x.HasStatusAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object _, string expected, CancellationToken _) => expected == status);
        return id;
    }

    private static ClaimsPrincipal User(string userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId), new Claim("sub", userId)], "test"));
    }

#pragma warning disable CS1998 // an async iterator over a fixed list
    private static async IAsyncEnumerable<T> AsAsync<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }
#pragma warning restore CS1998
}
