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

    [Theory]
    [InlineData("", "admin", "activate_promotion", false, "admin/activate_promotion")]
    [InlineData("search_promotions", "admin", "activate_promotion", false, "search_promotions\nadmin/activate_promotion")]
    [InlineData("activate_promotion", "admin", "activate_promotion", false, "activate_promotion")]
    [InlineData("admin/*", "admin", "activate_promotion", false, "admin/*")]
    [InlineData("admin/activate_promotion\nsearch_promotions", "admin", "activate_promotion", true, "search_promotions")]
    [InlineData("Admin/Activate_Promotion", "admin", "activate_promotion", true, "")]
    [InlineData("activate_promotion", "admin", "activate_promotion", true, "vendor/activate_promotion\nucp/activate_promotion")]
    [InlineData("activate_promotion\nvendor/activate_promotion", "admin", "activate_promotion", true, "vendor/activate_promotion\nucp/activate_promotion")]
    [InlineData("", "vendor", "*", false, "vendor/*")]
    [InlineData("vendor/*", "vendor", "*", true, "")]
    [InlineData("*", "vendor", "*", true, "admin/*\nucp/*")]
    public void UpdateSetting_Should_SwitchTheToolInOneScope_And_KeepTheOtherScopes(string setting, string scopeName, string toolName, bool enabled, string expected)
    {
        // Act
        var result = McpToolAvailabilityService.UpdateSetting(setting, scopeName, toolName, enabled, ["admin", "vendor", "ucp"]);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(!enabled, McpToolAvailabilityService.IsDisabled(McpToolAvailabilityService.ParseDisabledToolNames(result, scopeName), toolName));
    }
}
