using System.Security.Claims;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Security;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Extensions;

// Who requested a change: the user, or an operator signed in as the user, who must not approve their own change.
[Trait("Category", "Unit")]
public class McpClaimsPrincipalExtensionsTests
{
    private static readonly McpPendingChange Change = new() { Id = "change-1", RequestedByUserId = "operator-1" };

    public McpClaimsPrincipalExtensionsTests()
    {
        ClaimsPrincipalExtensions.UserIdClaimTypes = ["sub", ClaimTypes.NameIdentifier];
    }

    [Theory]
    [InlineData("operator-1", null, true)]
    [InlineData("approver-1", "operator-1", true)]
    [InlineData("approver-1", "OPERATOR-1", true)]
    [InlineData("approver-1", null, false)]
    [InlineData("approver-1", "operator-2", false)]
    public void HasRequested_Should_CountTheOperatorSignedInAsTheUser(string userId, string operatorUserId, bool expected)
    {
        // Arrange
        var identity = new ClaimsIdentity([new Claim("sub", userId), new Claim(ClaimTypes.NameIdentifier, userId)], "Bearer");
        if (operatorUserId != null)
        {
            identity.AddClaim(new Claim(PlatformConstants.Security.Claims.OperatorUserId, operatorUserId));
        }

        // Act
        var result = new ClaimsPrincipal(identity).HasRequested(Change);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HasRequested_Should_BeFalse_When_TheChangeHasNoRequester()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "approver-1")], "Bearer"));

        // Act & Assert
        Assert.False(user.HasRequested(new McpPendingChange { Id = "change-2" }));
    }
}
