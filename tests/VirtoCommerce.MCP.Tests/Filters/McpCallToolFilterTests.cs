using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Web.Filters;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Filters;

[Trait("Category", "Unit")]
public class McpCallToolFilterTests
{
    [Theory]
    [InlineData("admin", true)]
    [InlineData("Admin", true)]
    [InlineData("vendor", false)]
    [InlineData(null, false)]
    public void CanBeApprovedIn_Should_AllowOnlyTheAdminScope(string scopeName, bool expected)
    {
        // Act
        var result = McpCallToolFilter.CanBeApprovedIn(scopeName);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindToolsThatCannotBeApproved_Should_ReportApprovalToolsOutsideTheAdminScope()
    {
        // Arrange
        var tools = typeof(ApprovalTools).GetMethods()
            .Where(x => x.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
            .Select(x => McpServerTool.Create(x))
            .ToList();

        // Act
        var result = McpCallToolFilter.FindToolsThatCannotBeApproved(tools);

        // Assert
        Assert.Equal(
            [("vendor", "shared_change"), (null, "unscoped_change"), ("vendor", "vendor_change")],
            result.OrderBy(x => x.Name).ToArray());
    }

    [McpServerToolType]
    public static class ApprovalTools
    {
        [McpServerTool(Name = "admin_change"), Description("Approved in the admin scope.")]
        [McpScope(McpScopes.Admin)]
        [McpRequiresApproval]
        public static string AdminChange() => "admin";

        [McpServerTool(Name = "shared_change"), Description("Approved in the admin scope, refused in the vendor one.")]
        [McpScope(McpScopes.Admin, McpScopes.Vendor)]
        [McpRequiresApproval]
        public static string SharedChange() => "shared";

        [McpServerTool(Name = "vendor_change"), Description("Refused in the vendor scope.")]
        [McpScope(McpScopes.Vendor)]
        [McpRequiresApproval]
        public static string VendorChange() => "vendor";

        [McpServerTool(Name = "unscoped_change"), Description("Never held for approval.")]
        [McpRequiresApproval]
        public static string UnscopedChange() => "unscoped";

        [McpServerTool(Name = "vendor_read"), Description("Needs no approval.")]
        [McpScope(McpScopes.Vendor)]
        public static string VendorRead() => "read";
    }
}
