using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Filters;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Filters;

[Trait("Category", "Unit")]
public class McpScopeSessionConfiguratorTests
{
    [Fact]
    public void ApplyScope_Should_KeepOnlyTheToolsOfTheScope_And_ReportItsServerInfo()
    {
        // Arrange
        var (options, tools) = CreateOptions(typeof(AdminTools), typeof(UnscopedTools));

        // Act
        Apply(options, new McpScope { Name = McpScopes.Admin, Title = "Back office", Instructions = "Be careful." }, tools);

        // Assert
        Assert.Equal(["admin_tool", "shared_tool"], ToolNames(options));
        Assert.Equal("Back office", options.ServerInfo.Name);
        Assert.Equal("Be careful.", options.ServerInstructions);
    }

    [Fact]
    public void ApplyScope_Should_HideAdminTools_From_AnotherScope()
    {
        // Arrange
        var (options, tools) = CreateOptions(typeof(AdminTools), typeof(UnscopedTools));

        // Act
        Apply(options, new McpScope { Name = McpScopes.Vendor }, tools);

        // Assert
        Assert.Equal(["shared_tool", "vendor_tool"], ToolNames(options));
    }

    [Fact]
    public void ApplyScope_Should_KeepOnlyUnscopedTools_And_TheModuleServerInfo_When_TheEndpointHasNoScope()
    {
        // Arrange
        var (options, tools) = CreateOptions(typeof(AdminTools), typeof(UnscopedTools));
        options.ServerInstructions = "Module instructions";

        // Act
        Apply(options, scope: null, tools);

        // Assert
        Assert.Equal(["unscoped_tool"], ToolNames(options));
        Assert.Equal("Module instructions", options.ServerInstructions);
    }

    [Fact]
    public void ApplyScope_Should_GiveEachScopeItsOwnTool_When_TheSameNameIsUsedInDifferentScopes()
    {
        // Arrange: registered admin first, so the SDK alone would serve the admin tool and drop the vendor one.
        var (options, tools) = CreateOptions(typeof(AdminSearchTools), typeof(VendorSearchTools));

        // Act
        Apply(options, new McpScope { Name = McpScopes.Vendor }, tools);
        var vendorTool = Assert.Single(options.ToolCollection);
        Apply(options, new McpScope { Name = McpScopes.Admin }, tools);
        var adminTool = Assert.Single(options.ToolCollection);

        // Assert
        Assert.Equal("search", vendorTool.ProtocolTool.Name);
        Assert.Equal("Vendor search.", vendorTool.ProtocolTool.Description);
        Assert.Equal("Admin search.", adminTool.ProtocolTool.Description);
    }

    [Fact]
    public void FindDuplicateNames_Should_ReportOnlyNamesRepeatedWithinOneScope()
    {
        // Arrange
        var tools = CreateTools(typeof(AdminSearchTools), typeof(VendorSearchTools), typeof(DuplicateAdminSearchTools), typeof(UnscopedTools), typeof(DuplicateUnscopedTools));

        // Act
        var duplicates = McpScopeSessionConfigurator.FindDuplicateNames(tools);

        // Assert
        Assert.Equal(2, duplicates.Count);
        Assert.Contains((McpScopes.Admin, "search"), duplicates);
        Assert.Contains(((string)null, "unscoped_tool"), duplicates);
    }

    private static void Apply(McpServerOptions options, McpScope scope, IList<McpServerTool> tools)
    {
        McpScopeSessionConfigurator.ApplyScope(options, scope, tools, [], []);
    }

    private static (McpServerOptions Options, IList<McpServerTool> Tools) CreateOptions(params System.Type[] toolTypes)
    {
        var tools = CreateTools(toolTypes);

        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in tools)
        {
            collection.TryAdd(tool);
        }

        return (new McpServerOptions { ToolCollection = collection }, tools);
    }

    private static List<McpServerTool> CreateTools(params System.Type[] toolTypes)
    {
        return toolTypes
            .SelectMany(x => x.GetMethods())
            .Where(x => x.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
            .Select(x => McpServerTool.Create(x))
            .ToList();
    }

    private static string[] ToolNames(McpServerOptions options)
    {
        return options.ToolCollection.Select(x => x.ProtocolTool.Name).Order().ToArray();
    }

    [McpServerToolType]
    [McpScope(McpScopes.Admin)]
    public static class AdminTools
    {
        [McpServerTool(Name = "admin_tool"), Description("Admin only.")]
        public static string AdminTool() => "admin";

        [McpServerTool(Name = "shared_tool"), Description("Admin and vendor.")]
        [McpScope(McpScopes.Vendor)]
        public static string SharedTool() => "shared";
    }

    [McpServerToolType]
    public static class UnscopedTools
    {
        [McpServerTool(Name = "unscoped_tool"), Description("Mapped by a module on its own endpoint.")]
        public static string UnscopedTool() => "unscoped";

        [McpServerTool(Name = "vendor_tool"), Description("Vendor only.")]
        [McpScope(McpScopes.Vendor)]
        public static string VendorTool() => "vendor";
    }

    [McpServerToolType]
    [McpScope(McpScopes.Admin)]
    public static class AdminSearchTools
    {
        [McpServerTool(Name = "search"), Description("Admin search.")]
        public static string Search() => "admin";
    }

    [McpServerToolType]
    [McpScope(McpScopes.Vendor)]
    public static class VendorSearchTools
    {
        [McpServerTool(Name = "search"), Description("Vendor search.")]
        public static string Search() => "vendor";
    }

    [McpServerToolType]
    [McpScope("Admin")]
    public static class DuplicateAdminSearchTools
    {
        [McpServerTool(Name = "search"), Description("Another admin search.")]
        public static string Search() => "admin 2";
    }

    [McpServerToolType]
    public static class DuplicateUnscopedTools
    {
        [McpServerTool(Name = "unscoped_tool"), Description("Another unscoped tool.")]
        public static string UnscopedTool() => "unscoped 2";
    }
}
