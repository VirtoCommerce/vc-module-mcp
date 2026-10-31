using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpPublicOriginResolverTests
{
    private static readonly McpScope _scope = new() { Name = "vendor" };

    [Theory]
    [InlineData(null, "https://admin.example.com")]
    [InlineData("/vendor/mcp", "https://admin.example.com")]
    [InlineData(McpScope.HostEndpointPath, "http://vendor.example.com")]
    public async Task GetOriginAsync_Should_UseTheRequestHost_At_TheMcpPathOfAHost_When_TheConfiguredOriginIsAnotherHost(string endpointPath, string expected)
    {
        // Arrange
        var resolver = CreateResolver(new McpOptions { PublicOrigin = "https://admin.example.com" });
        var context = CreateContext("vendor.example.com", endpointPath);

        // Act
        var origin = await resolver.GetOriginAsync(context, _scope);

        // Assert
        Assert.Equal(expected, origin);
    }

    [Fact]
    public async Task GetOriginAsync_Should_KeepTheConfiguredOrigin_At_TheMcpPathOfItsOwnHost()
    {
        // Arrange
        var options = new McpOptions();
        options.Scopes[_scope.Name] = new McpScopeOptions { PublicOrigin = "https://vendor.example.com", Hosts = ["vendor.example.com"] };
        var resolver = CreateResolver(options);
        var context = CreateContext("vendor.example.com", McpScope.HostEndpointPath);

        // Act
        var origin = await resolver.GetOriginAsync(context, _scope);

        // Assert
        Assert.Equal("https://vendor.example.com", origin);
    }

    private static McpPublicOriginResolver CreateResolver(McpOptions options)
    {
        return new McpPublicOriginResolver(Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string host, string endpointPath)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(host);
        context.SetMcpScope(_scope);
        if (endpointPath != null)
        {
            context.SetMcpEndpointPath(endpointPath);
        }

        return context;
    }
}
