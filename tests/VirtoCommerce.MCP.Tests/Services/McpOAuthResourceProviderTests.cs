using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpOAuthResourceProviderTests
{
    [Fact]
    public async Task GetResourcesAsync_Should_ListTheEndpointOfEveryEnabledScope_AtItsPublicOrigin()
    {
        // Arrange
        var options = new McpOptions { PublicOrigin = "https://admin.example.com/" };
        options.Scopes["ucp"] = new McpScopeOptions { PublicOrigin = "https://shop.example.com" };
        options.Scopes["vendor"] = new McpScopeOptions { Enabled = false };
        var provider = CreateProvider(options,
            new McpScope { Name = "ucp", Title = "Shopping" },
            new McpScope { Name = "admin", Title = "Back office" },
            new McpScope { Name = "vendor" });

        // Act
        var resources = await provider.GetResourcesAsync(CreateContext(options));

        // Assert
        Assert.Equal(
            [
                ("https://admin.example.com/admin/mcp", "Back office", "mcp"),
                ("https://shop.example.com/ucp/mcp", "Shopping", "mcp"),
            ],
            resources.Select(x => (x.Uri, x.Title, x.Kind)));
    }

    [Fact]
    public async Task GetResourcesAsync_Should_AddTheMcpPathOfEveryHostOfTheScope()
    {
        // Arrange
        var options = new McpOptions();
        options.Scopes["ucp"] = new McpScopeOptions { Hosts = ["shop.example.com", "localhost:5173", " "] };
        var provider = CreateProvider(options, new McpScope { Name = "ucp" });

        // Act
        var resources = await provider.GetResourcesAsync(CreateContext(options));

        // Assert
        Assert.Equal(
            ["http://platform.local:10645/ucp/mcp", "https://shop.example.com/mcp", "http://localhost:5173/mcp"],
            resources.Select(x => x.Uri));
        Assert.All(resources, x => Assert.Equal("ucp", x.Title));
    }

    private static McpOAuthResourceProvider CreateProvider(McpOptions options, params McpScope[] scopes)
    {
        var wrapped = Options.Create(options);
        return new McpOAuthResourceProvider(new McpScopeRegistry(scopes, wrapped), wrapped);
    }

    private static DefaultHttpContext CreateContext(McpOptions options)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(options))
            .AddTransient<IMcpPublicOriginResolver, McpPublicOriginResolver>()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("platform.local", 10645);
        return context;
    }
}
