using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

// The tools a scope serves after a module built on another one removed or replaced some of them.
[Trait("Category", "Unit")]
public class McpToolCatalogTests
{
    private const string ShopScope = "shop";
    private const string OtherScope = "other";

    [Fact]
    public void GetTools_Should_ServeTheFirstRegistration_When_NoReplacementIsDeclared()
    {
        // Arrange
        var original = Tool("search_products", "original", ShopScope);
        var duplicate = Tool("search_products", "duplicate", ShopScope);

        // Act
        var tools = Catalog([original, duplicate]).GetTools(Scope(ShopScope)).ToList();

        // Assert: both are listed here; the server keeps the first of a name
        Assert.Equal([original, duplicate], tools);
        Assert.Single(Catalog([original, duplicate]).FindUnresolvedDuplicates());
    }

    [Fact]
    public void GetTools_Should_ServeTheLastRegistration_When_TheToolIsReplacedInTheScope()
    {
        // Arrange
        var original = Tool("search_products", "original", ShopScope, OtherScope);
        var replacement = Tool("search_products", "replacement", ShopScope);
        var catalog = Catalog([original, replacement], new McpToolOverride(ShopScope, "search_products", McpToolOverrideKind.Replace));

        // Act & Assert
        Assert.Equal([replacement], catalog.GetTools(Scope(ShopScope)).ToList());
        Assert.Equal([original], catalog.GetTools(Scope(OtherScope)).Where(x => x.IsInMcpScope(OtherScope)).ToList());
        Assert.Empty(catalog.FindUnresolvedDuplicates());
    }

    [Fact]
    public void GetTools_Should_LeaveARemovedToolOut_Of_ThatScopeOnly()
    {
        // Arrange
        var regions = Tool("list_regions", "regions", ShopScope, OtherScope);
        var products = Tool("search_products", "products", ShopScope);
        var catalog = Catalog([regions, products], new McpToolOverride("SHOP", "list_regions", McpToolOverrideKind.Remove));

        // Act & Assert
        Assert.Equal([products], catalog.GetTools(Scope(ShopScope)).ToList());
        Assert.Contains(regions, catalog.GetTools(Scope(OtherScope)));
    }

    [Fact]
    public void ScopeBuilder_Should_DeclareTheRemovalAndReplacement_For_AScopeOfAnotherModule()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.ConfigureMcpScope(ShopScope, scope => scope
            .ReplaceTool<ReplacementTools>("search_products")
            .RemoveTool("list_regions"));

        // Assert
        using var provider = services.BuildServiceProvider();
        var overrides = provider.GetServices<McpToolOverride>().ToList();
        Assert.Contains(overrides, x => x.Kind == McpToolOverrideKind.Replace && x.AppliesTo(ShopScope, "search_products"));
        Assert.Contains(overrides, x => x.Kind == McpToolOverrideKind.Remove && x.AppliesTo(ShopScope, "list_regions"));
        Assert.Contains(provider.GetServices<McpServerTool>(), x => x.ProtocolTool.Name == "search_products" && x.IsInMcpScope(ShopScope));
    }

    private static McpToolCatalog Catalog(McpServerTool[] tools, params McpToolOverride[] overrides)
    {
        return new McpToolCatalog(tools, overrides);
    }

    private static McpScope Scope(string name) => new() { Name = name };

    private static McpServerTool Tool(string name, string result, params string[] scopes)
    {
        return McpServerTool.Create(() => result, new McpServerToolCreateOptions { Name = name, Metadata = [new McpScopeAttribute(scopes)] });
    }

    [McpServerToolType]
    [McpScope(ShopScope)]
    public sealed class ReplacementTools
    {
        [McpServerTool(Name = "search_products")]
        public static string SearchProducts() => "replaced";
    }
}
