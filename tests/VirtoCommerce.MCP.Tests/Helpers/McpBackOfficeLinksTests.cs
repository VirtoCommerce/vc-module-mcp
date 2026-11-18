using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpBackOfficeLinksTests
{
    private const string Route = "/#!/workspace/store";
    private const string PublicOrigin = "https://admin.example.com";

    private static readonly McpScope _scope = new() { Name = "admin" };

    [Fact]
    public async Task GetLinkAsync_IsTheOriginOfTheScope_FollowedByTheRoute()
    {
        var context = CreateContext(_scope, CreateResolver($"{PublicOrigin}/"));

        Assert.Equal($"{PublicOrigin}{Route}", await McpBackOfficeLinks.GetLinkAsync(context, Route));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetLinkAsync_IsNull_WhenTheScopeHasNoOrigin(string origin)
    {
        var context = CreateContext(_scope, CreateResolver(origin));

        Assert.Null(await McpBackOfficeLinks.GetLinkAsync(context, Route));
    }

    // MCP:PublicOrigin is configured, so the origin resolver would give an origin even without a scope.
    [Fact]
    public async Task GetLinkAsync_IsNull_OutsideAnMcpRequest()
    {
        var resolver = new McpPublicOriginResolver(Options.Create(new McpOptions { PublicOrigin = PublicOrigin }));
        var context = CreateContext(scope: null, resolver);

        Assert.Null(await McpBackOfficeLinks.GetLinkAsync(context, Route));
    }

    [Fact]
    public async Task GetLinkAsync_IsNull_WithoutARequest()
    {
        Assert.Null(await McpBackOfficeLinks.GetLinkAsync(context: null, Route));
    }

    [Fact]
    public async Task GetLinkAsync_TakesTheOriginFromTheResolverTheScopeDeclared()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(CreateResolver("https://other.example.com"))
                .AddKeyedSingleton(McpScopeBuilder.GetServiceKey(_scope.Name), CreateResolver(PublicOrigin))
                .BuildServiceProvider(),
        };
        context.SetMcpScope(_scope);

        Assert.Equal($"{PublicOrigin}{Route}", await McpBackOfficeLinks.GetLinkAsync(context, Route));
    }

    [Theory]
    [InlineData("https://admin.example.com/#!/workspace/store", "Stores (https://admin.example.com/#!/workspace/store)")]
    [InlineData(null, "Stores")]
    [InlineData("", "Stores")]
    public void WithLink_PutsTheLinkInBrackets_WhenThereIsOne(string link, string expected)
    {
        Assert.Equal(expected, McpBackOfficeLinks.WithLink("Stores", link));
    }

    private static IMcpPublicOriginResolver CreateResolver(string origin)
    {
        var resolver = new Mock<IMcpPublicOriginResolver>();
        resolver.Setup(x => x.GetOriginAsync(It.IsAny<HttpContext>(), It.IsAny<McpScope>())).ReturnsAsync(origin);

        return resolver.Object;
    }

    private static DefaultHttpContext CreateContext(McpScope scope, IMcpPublicOriginResolver resolver)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(resolver).BuildServiceProvider(),
        };

        if (scope != null)
        {
            context.SetMcpScope(scope);
        }

        return context;
    }
}
