using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.MarketingExtensionSample.Services;
using VirtoCommerce.MCP.MarketingSample.Services;
using Xunit;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Tests;

[Trait("Category", "Unit")]
public class ModuleRegistrationTests
{
    [Fact]
    public void Initialize_Should_ReplaceTheAnalytics_And_AddTheToolsToTheAdminScope_After_TheSampleModule()
    {
        // Arrange: the platform initializes the sample first, because the extension depends on it
        var services = new ServiceCollection();
        services.AddLogging();
        new MarketingSample.Module().Initialize(services);
        new Module { Configuration = new ConfigurationBuilder().Build() }.Initialize(services);

        // Act
        using var provider = services.BuildServiceProvider();
        var analytics = provider.GetRequiredService<MarketingAnalytics>();
        var adminTools = provider.GetServices<McpServerTool>()
            .Where(x => x.IsInMcpScope(McpScopes.Admin))
            .Select(x => x.ProtocolTool.Name)
            .ToList();

        // Assert
        Assert.IsType<ContosoMarketingAnalytics>(analytics);
        Assert.Contains("set_promotion_cost_center", adminTools);
        Assert.Contains("get_margin_floors", adminTools);
        Assert.Contains("get_marketing_overview", adminTools);
        Assert.Equal(adminTools.Count, adminTools.Distinct().Count());
    }
}
