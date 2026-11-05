using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

// The browser origins a scope accepts: its own origin, the allowed ones, and the request's own on a loopback host only.
[Trait("Category", "Unit")]
public class McpOriginValidatorTests
{
    private static readonly McpScope ShopScope = new() { Name = "shop" };
    private static readonly McpScope OtherScope = new() { Name = "other" };

    [Theory]
    [InlineData("localhost:10650", "http://localhost:10650", true)]
    [InlineData("127.0.0.1:10650", "http://127.0.0.1:10650", true)]
    [InlineData("[::1]:10650", "http://[::1]:10650", true)]
    [InlineData("localhost:10650", "http://localhost:6274", false)]
    [InlineData("evil.example.test", "http://evil.example.test", false)]
    public async Task IsAllowedAsync_Should_TrustTheRequestsOwnOrigin_OnALoopbackHostOnly_When_NoOriginIsConfigured(string host, string origin, bool expected)
    {
        // Arrange
        var options = new McpOptions();

        // Act
        var allowed = await Validator(options).IsAllowedAsync(Context(options, host), ShopScope, origin);

        // Assert
        Assert.Equal(expected, allowed);
    }

    [Theory]
    [InlineData("https://shop.example.test", true)]
    [InlineData("https://SHOP.example.test:443", true)]
    [InlineData("http://shop.example.test", false)]
    [InlineData("https://shop.example.test.evil.test", false)]
    [InlineData("null", false)]
    [InlineData("file://", false)]
    public async Task IsAllowedAsync_Should_CompareWithTheConfiguredOrigin(string origin, bool expected)
    {
        // Arrange: the Host does not matter once the origin is configured
        var options = new McpOptions { PublicOrigin = "https://shop.example.test" };

        // Act
        var allowed = await Validator(options).IsAllowedAsync(Context(options, "localhost"), ShopScope, origin);

        // Assert
        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task IsAllowedAsync_Should_AllowTheConfiguredOrigins_ForTheirScopes()
    {
        // Arrange
        var options = new McpOptions
        {
            PublicOrigin = "https://admin.example.test",
            AllowedOrigins = ["https://everywhere.example.test"],
        };
        options.Scopes["shop"] = new McpScopeOptions { AllowedOrigins = ["https://inspector.example.test/"] };
        var validator = Validator(options);
        var context = Context(options, "admin.example.test");

        // Act & Assert
        Assert.True(await validator.IsAllowedAsync(context, ShopScope, "https://everywhere.example.test"));
        Assert.True(await validator.IsAllowedAsync(context, OtherScope, "https://everywhere.example.test"));
        Assert.True(await validator.IsAllowedAsync(context, ShopScope, "https://inspector.example.test"));
        Assert.False(await validator.IsAllowedAsync(context, OtherScope, "https://inspector.example.test"));
    }

    private static McpOriginValidator Validator(McpOptions options)
    {
        return new McpOriginValidator(Options.Create(options));
    }

    private static DefaultHttpContext Context(McpOptions options, string host)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(options))
            .AddTransient<IMcpPublicOriginResolver, McpPublicOriginResolver>()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(host);

        return context;
    }
}
