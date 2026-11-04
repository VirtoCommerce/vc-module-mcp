using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.MCP.Web.Middleware;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Middleware;

// An agent's token on the platform's other endpoints: looked up in the token store, so Disconnect ends it there too.
[Trait("Category", "Unit")]
public class McpAgentTokenMiddlewareTests
{
    private const string AgentAudience = "https://admin.example.com/admin/mcp";

    [Fact]
    public async Task InvokeAsync_Should_AnswerInvalidToken_When_TheAgentsTokenWasRevoked()
    {
        // Arrange
        var validator = new FixedTokenValidator(isActive: false);

        // Act
        var (user, context) = await InvokeWithContextAsync(Principal(AgentAudience), validator);

        // Assert
        Assert.Null(user);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", context.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal(1, validator.Lookups);
    }

    [Fact]
    public async Task InvokeAsync_Should_KeepTheUser_When_TheAgentsTokenIsActive()
    {
        // Arrange
        var validator = new FixedTokenValidator(isActive: true);

        // Act
        var user = await InvokeAsync(Principal("https://admin.example.com/mcp"), validator);

        // Assert
        Assert.True(user.Identity?.IsAuthenticated);
        Assert.Equal(1, validator.Lookups);
    }

    [Theory]
    [InlineData("resource_server")]
    [InlineData("https://admin.example.com/api")]
    [InlineData(null)]
    public async Task InvokeAsync_Should_NotLookUpOtherTokens(string audience)
    {
        // Arrange: a platform token, or a cookie or API key principal without an audience
        var validator = new FixedTokenValidator(isActive: false);

        // Act
        var user = await InvokeAsync(Principal(audience), validator);

        // Assert
        Assert.True(user.Identity?.IsAuthenticated);
        Assert.Equal(0, validator.Lookups);
    }

    [Fact]
    public async Task InvokeAsync_Should_NotLookUpTheToken_When_TurnedOff()
    {
        // Arrange
        var validator = new FixedTokenValidator(isActive: false);

        // Act
        var user = await InvokeAsync(Principal(AgentAudience), validator, new McpOptions { ValidateAgentTokensOnAllEndpoints = false });

        // Assert
        Assert.True(user.Identity?.IsAuthenticated);
        Assert.Equal(0, validator.Lookups);
    }

    private static async Task<ClaimsPrincipal> InvokeAsync(ClaimsPrincipal principal, McpTokenValidator validator, McpOptions options = null)
    {
        return (await InvokeWithContextAsync(principal, validator, options)).User;
    }

    // The user the rest of the pipeline sees, null when the middleware ended the request
    private static async Task<(ClaimsPrincipal User, HttpContext Context)> InvokeWithContextAsync(ClaimsPrincipal principal, McpTokenValidator validator, McpOptions options = null)
    {
        options ??= new McpOptions();
        var registry = new McpScopeRegistry([new McpScope { Name = McpScopes.Admin }], Options.Create(options));
        var context = new DefaultHttpContext { User = principal };
        ClaimsPrincipal seenByNext = null;
        var middleware = new McpAgentTokenMiddleware(next =>
        {
            seenByNext = next.User;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Options.Create(options), registry, validator);

        return (seenByNext, context);
    }

    private static ClaimsPrincipal Principal(string audience)
    {
        var identity = new ClaimsIdentity([new Claim("sub", "user-1")], "Bearer");
        if (audience != null)
        {
            identity.AddClaim(new Claim("aud", audience));
        }

        return new ClaimsPrincipal(identity);
    }

    private sealed class FixedTokenValidator(bool isActive) : McpTokenValidator(Mock.Of<IOpenIddictTokenManager>())
    {
        public int Lookups { get; private set; }

        public override Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            Lookups++;
            return Task.FromResult(isActive);
        }
    }
}
