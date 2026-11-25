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

// The links go through McpLinkResolver.GetAppLinkAsync, whose McpLinkResolverTests cover the cases without a link: no
// MCP request, no public origin, and the back office while the platform's admin UI is switched off, which leaves the
// Admin Shell its link.
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
        var links = CreateLinks(_backOffice, _adminShell);

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
        var links = CreateLinks(new AdminUIApp { Id = McpAdminShellLinks.AdminShellAppId, Path = "/apps/admin-shell", Url = "/", IsRoot = true });

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Equal($"{PublicOrigin}/", link);
    }

    [Fact]
    public async Task GetLinkAsync_Should_BeNull_WithoutTheAdminShell()
    {
        // Arrange
        var links = CreateLinks(_backOffice);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Null(link);
    }

    // VirtoCommerce:PlatformUI:Enable = false: no admin UI owns the site root and the AngularJS back office refuses its
    // page, but the platform still serves the Admin Shell at /apps/admin-shell/
    [Fact]
    public async Task GetLinkAsync_Should_BeTheAdminShell_When_ThePlatformsAdminUIIsSwitchedOff()
    {
        // Arrange
        var backOffice = new AdminUIApp { Id = McpLinkResolver.BackOfficeAppId, Path = "/apps/platform", Url = "/apps/platform/" };
        var links = CreateLinks(platformUIEnabled: false, backOffice, _adminShell);

        // Act
        var link = await links.GetLinkAsync();

        // Assert
        Assert.Equal($"{PublicOrigin}/apps/admin-shell/", link);
    }

    private static McpAdminShellLinks CreateLinks(params AdminUIApp[] apps)
    {
        return CreateLinks(platformUIEnabled: true, apps);
    }

    private static McpAdminShellLinks CreateLinks(bool platformUIEnabled, params AdminUIApp[] apps)
    {
        var originResolver = new Mock<IMcpPublicOriginResolver>();
        originResolver.Setup(x => x.GetOriginAsync(It.IsAny<HttpContext>(), It.IsAny<McpScope>())).ReturnsAsync($"{PublicOrigin}/");

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(originResolver.Object).BuildServiceProvider() };
        context.SetMcpScope(_scope);

        var adminUIs = new Mock<IAdminUIAppService>();
        adminUIs.Setup(x => x.GetAdminUIs()).Returns(apps);

        var options = new Mock<IOptionsMonitor<PlatformUIOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new PlatformUIOptions { Enable = platformUIEnabled });

        return new McpAdminShellLinks(new McpLinkResolver(new HttpContextAccessor { HttpContext = context }, adminUIs.Object, options.Object));
    }
}
