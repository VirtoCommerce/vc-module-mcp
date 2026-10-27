using System.Linq;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpToolAvailabilityServiceTests
{
    [Theory]
    [InlineData(null, "admin", new string[0])]
    [InlineData("", "admin", new string[0])]
    [InlineData("activate_promotion", "admin", new[] { "activate_promotion" })]
    [InlineData("activate_promotion", "vendor", new[] { "activate_promotion" })]
    [InlineData("admin/activate_promotion", "admin", new[] { "activate_promotion" })]
    [InlineData("admin/activate_promotion", "vendor", new string[0])]
    [InlineData("Admin / Activate_Promotion", "admin", new[] { "Activate_Promotion" })]
    [InlineData("vendor/*", "vendor", new[] { "*" })]
    [InlineData("a_tool\r\nb_tool, c_tool; vendor/d_tool", "admin", new[] { "a_tool", "b_tool", "c_tool" })]
    public void ParseDisabledToolNames_Should_KeepTheEntriesOfTheScope(string setting, string scopeName, string[] expected)
    {
        // Act
        var names = McpToolAvailabilityService.ParseDisabledToolNames(setting, scopeName);

        // Assert
        Assert.Equal(expected.Order(), names.Order());
    }

    [Theory]
    [InlineData("activate_promotion", "ACTIVATE_PROMOTION", true)]
    [InlineData("activate_promotion", "search_promotions", false)]
    [InlineData("*", "search_promotions", true)]
    public void IsDisabled_Should_MatchTheToolName_Or_TheWholeScope(string disabled, string toolName, bool expected)
    {
        // Act
        var result = McpToolAvailabilityService.IsDisabled(McpToolAvailabilityService.ParseDisabledToolNames(disabled, "admin"), toolName);

        // Assert
        Assert.Equal(expected, result);
    }
}
