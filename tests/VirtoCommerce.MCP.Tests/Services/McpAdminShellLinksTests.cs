using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpAdminShellLinksTests
{
    private const string PublicOrigin = "https://admin.example.com";

    private static readonly McpScope _scope = new() { Name = "admin" };

    private static readonly AdminUIApp _backOffice = new() { Id = McpLinkResolver.BackOfficeAppId, Url = "/", IsRoot = true };
    private static readonly AdminUIApp _adminShell = new() { Id = McpAdminShellLinks.AdminShellAppId, Path = "/apps/admin-shell", Url = "/apps/admin-shell/" };

    [Fact]
    public async Task GetLinkAsync_Should_BeTheAdminShellAtTheOriginOfTheScope()
    {
        // Arrange
        var links = CreateLinks(CreateContext(_scope, CreateOriginResolver($"{PublicOrigin}/")), _backOffice, _adminShell);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Equal($"{PublicOrigin}/apps/admin-shell/", link);
    }

    // RootApp = admin-shell: the platform serves the Admin Shell at the site root
    [Fact]
    public async Task GetLinkAsync_Should_BeTheSiteRoot_When_TheAdminShellOwnsIt()
    {
        // Arrange
        var adminShellAtTheRoot = new AdminUIApp { Id = McpAdminShellLinks.AdminShellAppId, Path = "/apps/admin-shell", Url = "/", IsRoot = true };
        var links = CreateLinks(CreateContext(_scope, CreateOriginResolver(PublicOrigin)), adminShellAtTheRoot);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Equal($"{PublicOrigin}/", link);
    }

    [Fact]
    public async Task GetLinkAsync_Should_BeNull_WithoutTheAdminShell()
    {
        // Arrange
        var links = CreateLinks(CreateContext(_scope, CreateOriginResolver(PublicOrigin)), _backOffice);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Null(link);
    }

    // VirtoCommerce:PlatformUI:Enable = false: the platform still lists the admin UIs, but its admin UI is off.
    [Fact]
    public async Task GetLinkAsync_Should_BeNull_WhenThePlatformsAdminUIIsSwitchedOff()
    {
        // Arrange
        var links = CreateLinks(CreateContext(_scope, CreateOriginResolver(PublicOrigin)), platformUIEnabled: false, _backOffice, _adminShell);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Null(link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetLinkAsync_Should_BeNull_When_TheScopeHasNoOrigin(string origin)
    {
        // Arrange
        var links = CreateLinks(CreateContext(_scope, CreateOriginResolver(origin)), _adminShell);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Null(link);
    }

    // MCP:PublicOrigin is configured, so the origin resolver would give an origin even without a scope.
    [Fact]
    public async Task GetLinkAsync_Should_BeNull_OutsideAnMcpRequest()
    {
        // Arrange
        var originResolver = new McpPublicOriginResolver(Options.Create(new McpOptions { PublicOrigin = PublicOrigin }));
        var links = CreateLinks(CreateContext(scope: null, originResolver), _adminShell);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Null(link);
    }

    private static McpAdminShellLinks CreateLinks(HttpContext context, params AdminUIApp[] apps)
    {
        return CreateLinks(context, platformUIEnabled: true, apps);
    }

    private static McpAdminShellLinks CreateLinks(HttpContext context, bool platformUIEnabled, params AdminUIApp[] apps)
    {
        var options = new Mock<IOptionsMonitor<PlatformUIOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new PlatformUIOptions { Enable = platformUIEnabled });

        var adminUIs = new Mock<IAdminUIAppService>();
        adminUIs.Setup(x => x.GetAdminUIs()).Returns(apps);

        return new McpAdminShellLinks(new HttpContextAccessor { HttpContext = context }, adminUIs.Object, options.Object);
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
