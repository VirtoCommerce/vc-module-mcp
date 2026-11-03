using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task IsActiveAsync_Should_RefuseATokenWithoutAnId_Unless_TheServerStoresNoTokens(bool disableTokenStorage, bool expected)
    {
        // Arrange
        var serverOptions = Options.Create(new OpenIddictServerOptions { DisableTokenStorage = disableTokenStorage });
        var validator = new McpTokenValidator(Mock.Of<IOpenIddictTokenManager>(), serverOptions);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "Bearer"));

        // Act
        var active = await validator.IsActiveAsync(principal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, active);
    }

    [Theory]
    [InlineData("user-1", "client_id", "agent-app", false)]
    [InlineData("agent-app", "client_id", "agent-app", true)]
    [InlineData("agent-app", "azp", "agent-app", true)]
    [InlineData("agent-app", "oi_prst", "agent-app", true)]
    [InlineData("", "client_id", "agent-app", true)]
    public void IsClientIdentity_Should_BeTrue_When_TheTokenHasNoUserBehindIt(string subject, string clientClaimType, string clientId, bool expected)
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject), new Claim(clientClaimType, clientId)], "Bearer"));

        // Act
        var isClient = McpTokenValidator.IsClientIdentity(principal);

        // Assert
        Assert.Equal(expected, isClient);
    }
}
