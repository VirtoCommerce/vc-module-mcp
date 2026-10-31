using System.Security.Claims;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.Platform.Core;
using Xunit;

namespace VirtoCommerce.MCP.Tests;

[Trait("Category", "Unit")]
public class McpClaimsPrincipalExtensionsTests
{
    private const string Permission = "marketing:read";
    private const string StoreScope = "MarketingStoreSelectedScope";

    [Fact]
    public void GetMcpPermissionGrant_Should_ReturnNull_When_ThePermissionIsNotGranted()
    {
        // Arrange
        var user = CreateUser("marketing:update");

        // Act & Assert
        Assert.Null(user.GetMcpPermissionGrant(Permission));
    }

    [Fact]
    public void GetMcpPermissionGrant_Should_BeGlobal_For_ABarePermissionClaim()
    {
        // Arrange
        var user = CreateUser(Permission);

        // Act
        var grant = user.GetMcpPermissionGrant(Permission);

        // Assert
        Assert.True(grant.IsGlobal);
        Assert.Empty(grant.GetScopeValues(StoreScope));
        Assert.True(grant.Allows(StoreScope, "any-store"));
    }

    [Fact]
    public void GetMcpPermissionGrant_Should_ReturnTheScopes_Of_AScopedPermissionClaim()
    {
        // Arrange
        var user = CreateUser(
            Permission + "|[{\"Type\":\"MarketingStoreSelectedScope\",\"Scope\":\"B2B-store\"}]",
            Permission + "|[{\"Type\":\"MarketingStoreSelectedScope\",\"Scope\":\"Electronics\"}]");

        // Act
        var grant = user.GetMcpPermissionGrant(Permission);

        // Assert
        Assert.False(grant.IsGlobal);
        Assert.Equal(["B2B-store", "Electronics"], grant.GetScopeValues(StoreScope));
        Assert.True(grant.Allows(StoreScope, "electronics"));
        Assert.False(grant.Allows(StoreScope, "Fashion"));
    }

    [Fact]
    public void GetMcpPermissionGrant_Should_NotBeGlobal_When_TheScopesCannotBeRead()
    {
        // Arrange
        var user = CreateUser(Permission + "|not json");

        // Act
        var grant = user.GetMcpPermissionGrant(Permission);

        // Assert
        Assert.False(grant.IsGlobal);
        Assert.Empty(grant.GetScopeValues(StoreScope));
    }

    [Fact]
    public void GetMcpPermissionGrant_Should_BeGlobal_For_AnAdministrator()
    {
        // Arrange
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, PlatformConstants.Security.SystemRoles.Administrator)], "test");

        // Act
        var grant = new ClaimsPrincipal(identity).GetMcpPermissionGrant(Permission);

        // Assert
        Assert.True(grant.IsGlobal);
    }

    private static ClaimsPrincipal CreateUser(params string[] permissionClaims)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var claim in permissionClaims)
        {
            identity.AddClaim(new Claim(PlatformConstants.Security.Claims.PermissionClaimType, claim));
        }

        return new ClaimsPrincipal(identity);
    }
}
