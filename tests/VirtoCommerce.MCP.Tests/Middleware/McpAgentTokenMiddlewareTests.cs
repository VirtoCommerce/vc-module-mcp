using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Middleware;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Middleware;

// An agent's token on the platform's other endpoints: looked up in the token store, so Disconnect ends it there too.
[Trait("Category", "Unit")]
public class McpAgentTokenMiddlewareTests
{
    private const string AgentAudience = "https://admin.example.com/admin/mcp";
    private const string AgentClientId = "agent-app";

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
    [InlineData("client_id")]
    [InlineData("azp")]
    public async Task InvokeAsync_Should_LookUpTheToken_When_ItWasIssuedToAnAgentWithoutAnMcpAudience(string clientClaimType)
    {
        // Arrange: a token of the agent's authorization requested without a resource
        var validator = new FixedTokenValidator(isActive: false);

        // Act
        var (user, context) = await InvokeWithContextAsync(Principal("resource_server", (clientClaimType, AgentClientId)), validator);

        // Assert
        Assert.Null(user);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(1, validator.Lookups);
    }

    [Theory]
    [InlineData("resource_server")]
    [InlineData("https://admin.example.com/api")]
    [InlineData(null)]
    public async Task InvokeAsync_Should_NotLookUpOtherTokens(string audience)
    {
        // Arrange: a platform token of another client, or a cookie or API key principal without an audience
        var validator = new FixedTokenValidator(isActive: false);

        // Act
        var user = await InvokeAsync(Principal(audience, ("client_id", "storefront")), validator);

        // Assert
        Assert.True(user.Identity?.IsAuthenticated);
        Assert.Equal(0, validator.Lookups);
    }

    [Fact]
    public async Task InvokeAsync_Should_LookUpTheToken_When_ItsScopeIsSwitchedOff()
    {
        // Arrange: tokens already issued for a scope stay an agent's after the scope is switched off
        var validator = new FixedTokenValidator(isActive: false);

        // Act
        var user = await InvokeAsync(Principal("https://admin.example.com/vendor/mcp"), validator);

        // Assert
        Assert.Null(user);
        Assert.Equal(1, validator.Lookups);
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
        var scopes = new[] { new McpScope { Name = McpScopes.Admin }, new McpScope { Name = McpScopes.Vendor } };
        var classifier = new McpAgentTokenClassifier(scopes, new FixedAgentService(AgentClientId), new MemoryCache(new MemoryCacheOptions()));
        var context = new DefaultHttpContext { User = principal };
        ClaimsPrincipal seenByNext = null;
        var middleware = new McpAgentTokenMiddleware(next =>
        {
            seenByNext = next.User;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Options.Create(options), classifier, validator);

        return (seenByNext, context);
    }

    private static ClaimsPrincipal Principal(string audience, params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity([new Claim("sub", "user-1")], "Bearer");
        if (audience != null)
        {
            identity.AddClaim(new Claim("aud", audience));
        }

        identity.AddClaims(claims.Select(x => new Claim(x.Type, x.Value)));

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

    // The client applications registered for an MCP resource
    private sealed class FixedAgentService(params string[] agentClientIds) : McpConnectedAgentService(null, null, null, null, null)
    {
        public override Task<bool> IsAgentClientAsync(string clientId)
        {
            return Task.FromResult(agentClientIds.Contains(clientId));
        }
    }
}
