using System.Security.Claims;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpTokenValidatorTests
{
    private const string Resource = "https://admin.example.com/admin/mcp";

    [Theory]
    [InlineData("https://admin.example.com/admin/mcp", true)]
    [InlineData("https://ADMIN.example.com/admin/mcp", true)]
    [InlineData("https://admin.example.com:443/admin/mcp", true)]
    [InlineData("https://admin.example.com/admin/mcp/", true)]
    [InlineData("https://admin.example.com/vendor/mcp", false)]
    [InlineData("https://admin.example.com/ucp/mcp", false)]
    [InlineData("https://admin.example.com/", false)]
    [InlineData("http://admin.example.com/admin/mcp", false)]
    [InlineData("https://admin.example.com:8443/admin/mcp", false)]
    [InlineData("https://other.example.com/admin/mcp", false)]
    [InlineData("https://admin.example.com/admin/mcp?x=1", false)]
    [InlineData("https://user@admin.example.com/admin/mcp", false)]
    [InlineData("resource_server", false)]
    public void HasExpectedAudience_Should_AcceptOnlyTheScopeResource(string audience, bool expected)
    {
        // Arrange
        var principal = Principal(new Claim("aud", audience));

        // Act
        var result = McpTokenValidator.HasExpectedAudience(principal, Resource);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HasExpectedAudience_Should_RejectATokenWithoutAudience()
    {
        // Arrange
        var principal = Principal(new Claim("sub", "user-1"));

        // Act
        var result = McpTokenValidator.HasExpectedAudience(principal, Resource);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasExpectedAudience_Should_AcceptAToken_When_OneOfItsAudiencesIsTheResource()
    {
        // Arrange
        var principal = Principal(new Claim("aud", "https://admin.example.com/ucp/mcp"), new Claim("aud", Resource));

        // Act
        var result = McpTokenValidator.HasExpectedAudience(principal, Resource);

        // Assert
        Assert.True(result);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }
}
