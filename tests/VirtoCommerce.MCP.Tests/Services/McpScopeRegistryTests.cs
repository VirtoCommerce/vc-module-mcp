using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Data.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpScopeRegistryTests
{
    [Theory]
    [InlineData("/admin/mcp", McpScopes.Admin)]
    [InlineData("/ADMIN/MCP", McpScopes.Admin)]
    [InlineData("/admin/mcp/", McpScopes.Admin)]
    [InlineData("/vendor/mcp", McpScopes.Vendor)]
    [InlineData("/ucp/mcp", null)]
    [InlineData("/admin", null)]
    [InlineData("/admin/mcp/extra", null)]
    [InlineData("/mcp/admin", null)]
    [InlineData("/", null)]
    public void FindScopeByPath_Should_MatchOnlyTheEndpointsOfRegisteredScopes(string path, string expectedScope)
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var scope = registry.FindScopeByPath(new PathString(path));

        // Assert
        Assert.Equal(expectedScope, scope?.Name);
    }

    [Fact]
    public void GetScope_Should_NotReturnAScope_When_ItIsDisabledInConfiguration()
    {
        // Arrange
        var options = new McpOptions();
        options.Scopes[McpScopes.Vendor] = new McpScopeOptions { Enabled = false };
        var registry = CreateRegistry(options);

        // Act & Assert
        Assert.Null(registry.GetScope(McpScopes.Vendor));
        Assert.Null(registry.FindScopeByPath(new PathString("/vendor/mcp")));
        Assert.NotNull(registry.GetScope(McpScopes.Admin));
    }

    [Fact]
    public void Scopes_Should_KeepTheLastRegistration_When_ANameIsRegisteredTwice()
    {
        // Arrange
        var registry = new McpScopeRegistry(
            [
                new McpScope { Name = McpScopes.Admin, Title = "Built-in" },
                new McpScope { Name = "Admin", Title = "Replaced" },
            ],
            Options.Create(new McpOptions()));

        // Act & Assert
        Assert.Single(registry.Scopes);
        Assert.Equal("Replaced", registry.GetScope(McpScopes.Admin).Title);
    }

    private static McpScopeRegistry CreateRegistry(McpOptions options = null)
    {
        return new McpScopeRegistry(
            [
                new McpScope { Name = McpScopes.Admin },
                new McpScope { Name = McpScopes.Vendor },
            ],
            Options.Create(options ?? new McpOptions()));
    }
}
