using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpLinkResolverTests
{
    private const string Route = "/#!/workspace/store";
    private const string PublicOrigin = "https://admin.example.com";

    private static readonly McpScope _scope = new() { Name = "admin" };

    [Fact]
    public async Task GetLinkAsync_Should_BeTheOriginOfTheScope_FollowedByTheRoute()
    {
        // Arrange
        var resolver = CreateResolver(CreateContext(_scope, CreateOriginResolver($"{PublicOrigin}/")));

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Equal($"{PublicOrigin}{Route}", link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetLinkAsync_Should_BeNull_When_TheScopeHasNoOrigin(string origin)
    {
        // Arrange
        var resolver = CreateResolver(CreateContext(_scope, CreateOriginResolver(origin)));

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Null(link);
    }

    // MCP:PublicOrigin is configured, so the origin resolver would give an origin even without a scope.
    [Fact]
    public async Task GetLinkAsync_Should_BeNull_OutsideAnMcpRequest()
    {
        // Arrange
        var originResolver = new McpPublicOriginResolver(Options.Create(new McpOptions { PublicOrigin = PublicOrigin }));
        var resolver = CreateResolver(CreateContext(scope: null, originResolver));

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Null(link);
    }

    [Fact]
    public async Task GetLinkAsync_Should_BeNull_WithoutARequest()
    {
        // Arrange
        var resolver = CreateResolver(context: null);

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Null(link);
    }

    [Fact]
    public async Task GetLinkAsync_Should_TakeTheOriginFromTheResolverTheScopeDeclared()
    {
        // Arrange
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(CreateOriginResolver("https://other.example.com"))
                .AddKeyedSingleton(McpScopeBuilder.GetServiceKey(_scope.Name), CreateOriginResolver(PublicOrigin))
                .BuildServiceProvider(),
        };
        context.SetMcpScope(_scope);
        var resolver = CreateResolver(context);

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Equal($"{PublicOrigin}{Route}", link);
    }

    // Admin Shell owns the site root (RootApp = admin-shell, or no AngularJS admin at the root): the platform serves the
    // AngularJS back office at /apps/platform/, and the link goes there.
    [Fact]
    public async Task GetLinkAsync_Should_FollowTheBackOffice_WhereThePlatformServesIt()
    {
        // Arrange
        var adminUIs = CreateAdminUIs(
            new AdminUIApp { Id = "admin-shell", Url = "/", IsRoot = true },
            new AdminUIApp { Id = McpLinkResolver.BackOfficeAppId, Url = "/apps/platform/" });
        var resolver = CreateResolver(CreateContext(_scope, CreateOriginResolver(PublicOrigin)), adminUIs);

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Equal($"{PublicOrigin}/apps/platform{Route}", link);
    }

    [Fact]
    public async Task GetLinkAsync_Should_BeNull_WithoutTheBackOffice()
    {
        // Arrange
        var adminUIs = CreateAdminUIs(new AdminUIApp { Id = "admin-shell", Url = "/", IsRoot = true });
        var resolver = CreateResolver(CreateContext(_scope, CreateOriginResolver(PublicOrigin)), adminUIs);

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Null(link);
    }

    // VirtoCommerce:PlatformUI:Enable = false: the platform still lists the back office, but it refuses its page.
    [Fact]
    public async Task GetLinkAsync_Should_BeNull_WhenThePlatformsAdminUIIsSwitchedOff()
    {
        // Arrange
        var resolver = CreateResolver(CreateContext(_scope, CreateOriginResolver(PublicOrigin)), platformUIEnabled: false);

        // Act
        var link = await resolver.GetLinkAsync(Route);

        // Assert
        Assert.Null(link);
    }

    private static McpLinkResolver CreateResolver(HttpContext context, IAdminUIAppService adminUIs = null, bool platformUIEnabled = true)
    {
        var options = new Mock<IOptionsMonitor<PlatformUIOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new PlatformUIOptions { Enable = platformUIEnabled });

        adminUIs ??= CreateAdminUIs(new AdminUIApp { Id = McpLinkResolver.BackOfficeAppId, Url = "/", IsRoot = true });

        return new McpLinkResolver(new HttpContextAccessor { HttpContext = context }, adminUIs, options.Object);
    }

    private static IAdminUIAppService CreateAdminUIs(params AdminUIApp[] apps)
    {
        var adminUIs = new Mock<IAdminUIAppService>();
        adminUIs.Setup(x => x.GetAdminUIs()).Returns(apps);

        return adminUIs.Object;
    }

    private static IMcpPublicOriginResolver CreateOriginResolver(string origin)
    {
        var resolver = new Mock<IMcpPublicOriginResolver>();
        resolver.Setup(x => x.GetOriginAsync(It.IsAny<HttpContext>(), It.IsAny<McpScope>())).ReturnsAsync(origin);

        return resolver.Object;
    }

    private static DefaultHttpContext CreateContext(McpScope scope, IMcpPublicOriginResolver originResolver)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(originResolver).BuildServiceProvider(),
        };

        if (scope != null)
        {
            context.SetMcpScope(scope);
        }

        return context;
    }
}
