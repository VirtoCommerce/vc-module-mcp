using System.Threading.Tasks;
using Moq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpPageSizeResolverTests
{
    [Theory]
    [InlineData(null, 20)]
    [InlineData(5, 5)]
    [InlineData(500, 50)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public async Task GetPageSizeAsync_Should_UseTheDefault_And_CapTheRequestedTake(int? take, int expected)
    {
        // Arrange
        var resolver = new McpPageSizeResolver(Mock.Of<ISettingsManager>());

        // Act
        var result = await resolver.GetPageSizeAsync(take);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetPageSizeAsync_Should_UseTheConfiguredMaxTake()
    {
        // Arrange
        var setting = ModuleConstants.Settings.Tools.MaxTake;
        var settingsManager = new Mock<ISettingsManager>();
        settingsManager
            .Setup(x => x.GetObjectSettingAsync(setting.Name, null, null))
            .ReturnsAsync(new ObjectSettingEntry { Name = setting.Name, Value = 10 });
        var resolver = new McpPageSizeResolver(settingsManager.Object);

        // Act
        var defaultTake = await resolver.GetPageSizeAsync(null);
        var cappedTake = await resolver.GetPageSizeAsync(1000);

        // Assert
        Assert.Equal(10, defaultTake);
        Assert.Equal(10, cappedTake);
    }
}
