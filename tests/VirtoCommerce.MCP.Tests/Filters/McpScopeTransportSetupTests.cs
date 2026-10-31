using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Web.Extensions;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Filters;

[Trait("Category", "Unit")]
public class McpScopeTransportSetupTests
{
    [Fact]
    public async Task ConfigureSessionOptions_Should_KeepTheScopeIsolation_When_AnotherModuleSetsItsOwnHandlerLater()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScopedMcpServer();
        var otherHandlerCalled = false;
        services.Configure<HttpServerTransportOptions>(options => options.ConfigureSessionOptions = (_, _, _) =>
        {
            otherHandlerCalled = true;
            return Task.CompletedTask;
        });
        services.AddSingleton(McpServerTool.Create(() => "scoped", new McpServerToolCreateOptions { Name = "scoped_tool" }));
        using var provider = services.BuildServiceProvider();
        var transportOptions = provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        var serverOptions = new McpServerOptions { ToolCollection = [] };
        var httpContext = new DefaultHttpContext { RequestServices = provider };

        // Act
        await transportOptions.ConfigureSessionOptions!(httpContext, serverOptions, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(otherHandlerCalled);
        Assert.True(serverOptions.ToolCollection.TryGetPrimitive("scoped_tool", out _));
    }
}
